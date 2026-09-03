using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TornadoEos.Core.Abstractions;
using TornadoEos.Core.Exceptions;
using TornadoEos.Core.Logging;
using TornadoEos.Core.Models;

namespace TornadoEos.Core.Services;

/// <summary>
/// High-level façade over an <see cref="ICameraBackend"/>. It owns the connection
/// lifecycle, exposes UI-friendly events, and implements the headline Tornado-EOS
/// workflow: connect to the camera and set its menu language to a chosen language,
/// transparently entering service mode and lifting the regional language lock when
/// required.
/// </summary>
public sealed class CameraService : IDisposable
{
    private readonly ICameraBackend _backend;
    private CameraInfo? _info;

    public CameraService(ICameraBackend backend)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _backend.Log += OnBackendLog;
    }

    public ConnectionState State { get; private set; } = ConnectionState.Disconnected;

    public CameraInfo? CameraInfo => _info;

    public CameraLanguage? CurrentLanguage { get; private set; }

    public bool IsLanguageLockEnabled { get; private set; }

    public bool SupportsMenuLanguageWrite => _backend.SupportsMenuLanguageWrite;

    public bool IsSimulation => _backend.IsSimulation;

    public event EventHandler<ConnectionState>? StateChanged;
    public event EventHandler<LogEntry>? LogReceived;
    public event EventHandler<ServiceProgress>? ProgressChanged;
    public event EventHandler<CameraLanguage>? CurrentLanguageChanged;

    public async Task<CameraInfo> ConnectAsync(CancellationToken cancellationToken = default)
    {
        SetState(ConnectionState.Connecting);
        try
        {
            _info = await _backend.ConnectAsync(cancellationToken).ConfigureAwait(false);
            IsLanguageLockEnabled = await _backend.GetLanguageLockAsync(cancellationToken).ConfigureAwait(false);
            SetCurrentLanguage(await _backend.GetCurrentLanguageAsync(cancellationToken).ConfigureAwait(false));
            SetState(ConnectionState.Connected);
            return _info;
        }
        catch
        {
            SetState(ConnectionState.Error);
            throw;
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        await _backend.DisconnectAsync(cancellationToken).ConfigureAwait(false);
        _info = null;
        CurrentLanguage = null;
        SetState(ConnectionState.Disconnected);
    }

    public async Task EnsureServiceModeAsync(CancellationToken cancellationToken = default)
    {
        EnsureConnected();
        if (_backend.IsInServiceMode)
            return;
        SetState(ConnectionState.Busy);
        try
        {
            await _backend.EnterServiceModeAsync(cancellationToken).ConfigureAwait(false);
            SetState(ConnectionState.ServiceMode);
        }
        catch
        {
            // Roll the state back so the UI does not get stuck in "busy".
            SetState(_info is null ? ConnectionState.Disconnected : ConnectionState.Connected);
            throw;
        }
    }

    public Task<IReadOnlyList<CameraLanguage>> GetAvailableLanguagesAsync(CancellationToken cancellationToken = default)
    {
        EnsureConnected();
        return _backend.GetAvailableLanguagesAsync(cancellationToken);
    }

    /// <summary>
    /// Enables/disables the regional language lock (service mode is entered first).
    /// </summary>
    public async Task SetLanguageLockAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        await EnsureServiceModeAsync(cancellationToken).ConfigureAwait(false);
        SetState(ConnectionState.Busy);
        try
        {
            await _backend.SetLanguageLockAsync(enabled, ToProgress(), cancellationToken).ConfigureAwait(false);
            IsLanguageLockEnabled = enabled;
        }
        finally
        {
            SetState(ConnectionState.ServiceMode);
        }
    }

    /// <summary>
    /// The headline feature: set the camera menu language to the specified language.
    /// Enters service mode if needed and, when <paramref name="autoUnlock"/> is true,
    /// lifts the regional language lock automatically when the target language is not
    /// otherwise selectable.
    /// </summary>
    public async Task SetMenuLanguageAsync(
        CameraLanguage language,
        bool autoUnlock = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(language);
        await EnsureServiceModeAsync(cancellationToken).ConfigureAwait(false);

        var available = await _backend.GetAvailableLanguagesAsync(cancellationToken).ConfigureAwait(false);
        bool selectable = available.Any(l => l.IsoCode == language.IsoCode);
        if (!selectable)
        {
            if (!autoUnlock)
                throw new UnsupportedLanguageException(language.IsoCode);

            RaiseLog(LogLevel.Info,
                $"“{language.ChineseName}”被区域语言锁隐藏，正在关闭语言锁以显示该语言。");
            await SetLanguageLockAsync(false, cancellationToken).ConfigureAwait(false);
        }

        SetState(ConnectionState.Busy);
        try
        {
            await _backend.SetMenuLanguageAsync(language, ToProgress(), cancellationToken).ConfigureAwait(false);
            SetCurrentLanguage(language);
        }
        finally
        {
            SetState(ConnectionState.ServiceMode);
        }
    }

    /// <summary>Convenience overload that resolves the language by ISO code.</summary>
    public Task SetMenuLanguageAsync(string isoCode, bool autoUnlock = true, CancellationToken cancellationToken = default)
    {
        var language = CameraLanguages.FindByIso(isoCode)
            ?? throw new UnsupportedLanguageException(isoCode);
        return SetMenuLanguageAsync(language, autoUnlock, cancellationToken);
    }

    public void Dispose()
    {
        _backend.Log -= OnBackendLog;
        _backend.Dispose();
    }

    private void EnsureConnected()
    {
        if (_info is null)
            throw new CameraNotConnectedException();
    }

    private IProgress<ServiceProgress> ToProgress() =>
        new Progress<ServiceProgress>(p => ProgressChanged?.Invoke(this, p));

    private void SetState(ConnectionState state)
    {
        State = state;
        StateChanged?.Invoke(this, state);
    }

    private void SetCurrentLanguage(CameraLanguage language)
    {
        CurrentLanguage = language;
        CurrentLanguageChanged?.Invoke(this, language);
    }

    private void RaiseLog(LogLevel level, string message) =>
        LogReceived?.Invoke(this, LogEntry.Now(level, message));

    private void OnBackendLog(object? sender, LogEntry entry) =>
        LogReceived?.Invoke(this, entry);
}
