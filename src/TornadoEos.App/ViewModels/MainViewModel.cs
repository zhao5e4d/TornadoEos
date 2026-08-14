using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using TornadoEos.App.Mvvm;
using TornadoEos.Core.Logging;
using TornadoEos.Core.Models;
using TornadoEos.Core.Services;
using TornadoEos.Hardware;

namespace TornadoEos.App.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly CameraService _service;
    private readonly Dispatcher _dispatcher;

    private bool _isConnected;
    private bool _isBusy;
    private bool _isDiagnosticsRunning;
    private string _connectionStatus = "Disconnected";
    private string _cameraModel = "—";
    private string _serialNumber = "—";
    private string _firmwareVersion = "—";
    private string _batteryText = "—";
    private string _portDescription = "—";
    private string _currentLanguageText = "—";
    private bool _languageLockEnabled;
    private bool _autoUnlock = true;
    private CameraLanguage? _selectedLanguage;
    private int _progressValue;
    private string _progressText = string.Empty;
    private bool _isProgressVisible;
    private string _diagnosticsSummary = "Run diagnostics to inspect WPD, MTP, and EOS properties (read-only).";
    private string _diagnosticsReport = string.Empty;

    public MainViewModel() : this(new CameraService(new TornadoEos.Hardware.WpdCameraBackend()))
    {
    }

    public MainViewModel(CameraService service)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _dispatcher = Dispatcher.CurrentDispatcher;

        foreach (var language in CameraLanguages.All)
            Languages.Add(language);
        _selectedLanguage = CameraLanguages.FindByIso("en");

        _service.LogReceived += OnLog;
        _service.StateChanged += OnStateChanged;
        _service.ProgressChanged += OnProgress;
        _service.CurrentLanguageChanged += OnCurrentLanguageChanged;

        ConnectCommand = new AsyncRelayCommand(ConnectAsync, () => !IsConnected && !IsBusy && !IsDiagnosticsRunning);
        DisconnectCommand = new AsyncRelayCommand(DisconnectAsync, () => IsConnected && !IsBusy && !IsDiagnosticsRunning);
        ApplyLanguageCommand = new AsyncRelayCommand(ApplyLanguageAsync, () => IsConnected && !IsBusy && !IsDiagnosticsRunning && SelectedLanguage is not null);
        RunDiagnosticsCommand = new AsyncRelayCommand(RunDiagnosticsAsync, () => !IsBusy && !IsDiagnosticsRunning);
        CopyDiagnosticsCommand = new AsyncRelayCommand(CopyDiagnosticsAsync, () => HasDiagnosticsReport && !IsDiagnosticsRunning);
        SaveDiagnosticsCommand = new AsyncRelayCommand(SaveDiagnosticsAsync, () => HasDiagnosticsReport && !IsDiagnosticsRunning);

        Log(LogLevel.Info, "Tornado EOS ready. Connect a camera to begin.");
    }

    public ObservableCollection<CameraLanguage> Languages { get; } = new();

    public ObservableCollection<LogEntry> LogEntries { get; } = new();

    public AsyncRelayCommand ConnectCommand { get; }
    public AsyncRelayCommand DisconnectCommand { get; }
    public AsyncRelayCommand ApplyLanguageCommand { get; }
    public AsyncRelayCommand RunDiagnosticsCommand { get; }
    public AsyncRelayCommand CopyDiagnosticsCommand { get; }
    public AsyncRelayCommand SaveDiagnosticsCommand { get; }

    public bool IsConnected
    {
        get => _isConnected;
        private set { if (SetProperty(ref _isConnected, value)) RaiseCommandStates(); }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set { if (SetProperty(ref _isBusy, value)) RaiseCommandStates(); }
    }

    public string ConnectionStatus
    {
        get => _connectionStatus;
        private set => SetProperty(ref _connectionStatus, value);
    }

    public string CameraModel { get => _cameraModel; private set => SetProperty(ref _cameraModel, value); }
    public string SerialNumber { get => _serialNumber; private set => SetProperty(ref _serialNumber, value); }
    public string FirmwareVersion { get => _firmwareVersion; private set => SetProperty(ref _firmwareVersion, value); }
    public string BatteryText { get => _batteryText; private set => SetProperty(ref _batteryText, value); }
    public string PortDescription { get => _portDescription; private set => SetProperty(ref _portDescription, value); }

    public string CurrentLanguageText
    {
        get => _currentLanguageText;
        private set => SetProperty(ref _currentLanguageText, value);
    }

    public bool LanguageLockEnabled
    {
        get => _languageLockEnabled;
        private set => SetProperty(ref _languageLockEnabled, value);
    }

    public bool AutoUnlock
    {
        get => _autoUnlock;
        set => SetProperty(ref _autoUnlock, value);
    }

    public CameraLanguage? SelectedLanguage
    {
        get => _selectedLanguage;
        set { if (SetProperty(ref _selectedLanguage, value)) ApplyLanguageCommand.RaiseCanExecuteChanged(); }
    }

    public int ProgressValue { get => _progressValue; private set => SetProperty(ref _progressValue, value); }
    public string ProgressText { get => _progressText; private set => SetProperty(ref _progressText, value); }
    public bool IsProgressVisible { get => _isProgressVisible; private set => SetProperty(ref _isProgressVisible, value); }

    public bool IsDiagnosticsRunning
    {
        get => _isDiagnosticsRunning;
        private set
        {
            if (SetProperty(ref _isDiagnosticsRunning, value))
                RaiseCommandStates();
        }
    }

    public string DiagnosticsSummary
    {
        get => _diagnosticsSummary;
        private set => SetProperty(ref _diagnosticsSummary, value);
    }

    public string DiagnosticsReport
    {
        get => _diagnosticsReport;
        private set
        {
            if (SetProperty(ref _diagnosticsReport, value))
            {
                OnPropertyChanged(nameof(HasDiagnosticsReport));
                RaiseCommandStates();
            }
        }
    }

    public bool HasDiagnosticsReport => !string.IsNullOrWhiteSpace(DiagnosticsReport);

    private async Task ConnectAsync()
    {
        try
        {
            var info = await _service.ConnectAsync();
            CameraModel = info.ModelName;
            SerialNumber = info.SerialNumber;
            FirmwareVersion = info.FirmwareVersion;
            BatteryText = info.BatteryPercent >= 0 ? $"{info.BatteryPercent}%" : "N/A";
            PortDescription = info.PortDescription;
            LanguageLockEnabled = _service.IsLanguageLockEnabled;
        }
        catch (Exception ex)
        {
            Log(LogLevel.Error, $"Connect failed: {ex.Message}");
        }
    }

    private async Task DisconnectAsync()
    {
        try
        {
            await _service.DisconnectAsync();
            CameraModel = SerialNumber = FirmwareVersion = BatteryText = PortDescription = "—";
            CurrentLanguageText = "—";
        }
        catch (Exception ex)
        {
            Log(LogLevel.Error, $"Disconnect failed: {ex.Message}");
        }
    }

    private async Task ApplyLanguageAsync()
    {
        var target = SelectedLanguage;
        if (target is null)
            return;
        try
        {
            IsProgressVisible = true;
            await _service.SetMenuLanguageAsync(target, AutoUnlock);
            LanguageLockEnabled = _service.IsLanguageLockEnabled;
        }
        catch (Exception ex)
        {
            Log(LogLevel.Error, $"Failed to set language: {ex.Message}");
        }
        finally
        {
            IsProgressVisible = false;
            ProgressValue = 0;
            ProgressText = string.Empty;
        }
    }

    private async Task RunDiagnosticsAsync()
    {
        try
        {
            IsDiagnosticsRunning = true;
            Log(LogLevel.Info, "Starting read-only camera diagnostics (WPD + MTP + EOS)...");

            if (IsConnected)
            {
                Log(LogLevel.Info, "Disconnecting first so diagnostics can open an exclusive USB session.");
                await _service.DisconnectAsync().ConfigureAwait(true);
                CameraModel = SerialNumber = FirmwareVersion = BatteryText = PortDescription = "—";
                CurrentLanguageText = "—";
            }

            var result = await Task.Run(() => CameraDiagnostics.Run(msg =>
                Log(LogLevel.Debug, msg))).ConfigureAwait(true);

            DiagnosticsReport = result.ReportText;
            DiagnosticsSummary = result.SummaryText;
            Log(LogLevel.Info, $"Diagnostics complete. {result.SummaryText}");
        }
        catch (Exception ex)
        {
            Log(LogLevel.Error, $"Diagnostics failed: {ex.Message}");
            DiagnosticsSummary = "Diagnostics failed — see activity log.";
        }
        finally
        {
            IsDiagnosticsRunning = false;
        }
    }

    private Task CopyDiagnosticsAsync()
    {
        if (HasDiagnosticsReport)
        {
            Clipboard.SetText(DiagnosticsReport);
            Log(LogLevel.Info, "Diagnostics report copied to clipboard.");
        }
        return Task.CompletedTask;
    }

    private Task SaveDiagnosticsAsync()
    {
        if (!HasDiagnosticsReport)
            return Task.CompletedTask;

        var dialog = new SaveFileDialog
        {
            Title = "Save diagnostics report",
            Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
            FileName = $"tornado-eos-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.txt",
            DefaultExt = ".txt",
        };

        if (dialog.ShowDialog() != true)
            return Task.CompletedTask;

        File.WriteAllText(dialog.FileName, DiagnosticsReport);
        Log(LogLevel.Info, $"Diagnostics report saved to {dialog.FileName}");
        return Task.CompletedTask;
    }

    private void OnStateChanged(object? sender, ConnectionState state) => OnUi(() =>
    {
        ConnectionStatus = state switch
        {
            ConnectionState.Disconnected => "Disconnected",
            ConnectionState.Connecting => "Connecting…",
            ConnectionState.Connected => "Connected",
            ConnectionState.ServiceMode => "Service mode",
            ConnectionState.Busy => "Working…",
            ConnectionState.Error => "Error",
            _ => state.ToString(),
        };
        IsConnected = state is ConnectionState.Connected or ConnectionState.ServiceMode or ConnectionState.Busy;
        IsBusy = state is ConnectionState.Connecting or ConnectionState.Busy;
    });

    private void OnProgress(object? sender, ServiceProgress p) => OnUi(() =>
    {
        ProgressValue = p.Percent;
        ProgressText = p.Message;
    });

    private void OnCurrentLanguageChanged(object? sender, CameraLanguage language) => OnUi(() =>
        CurrentLanguageText = $"{language.EnglishName} ({language.NativeName})");

    private void OnLog(object? sender, LogEntry entry) => OnUi(() => LogEntries.Add(entry));

    private void Log(LogLevel level, string message) =>
        OnUi(() => LogEntries.Add(LogEntry.Now(level, message)));

    private void RaiseCommandStates()
    {
        ConnectCommand.RaiseCanExecuteChanged();
        DisconnectCommand.RaiseCanExecuteChanged();
        ApplyLanguageCommand.RaiseCanExecuteChanged();
        RunDiagnosticsCommand.RaiseCanExecuteChanged();
        CopyDiagnosticsCommand.RaiseCanExecuteChanged();
        SaveDiagnosticsCommand.RaiseCanExecuteChanged();
    }

    private void OnUi(Action action)
    {
        if (_dispatcher.CheckAccess())
            action();
        else
            _dispatcher.Invoke(action);
    }

    public void Dispose()
    {
        _service.LogReceived -= OnLog;
        _service.StateChanged -= OnStateChanged;
        _service.ProgressChanged -= OnProgress;
        _service.CurrentLanguageChanged -= OnCurrentLanguageChanged;
        _service.Dispose();
    }
}
