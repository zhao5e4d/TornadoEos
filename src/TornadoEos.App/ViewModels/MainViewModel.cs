using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using TornadoEos.App.Mvvm;
using TornadoEos.Core.Backends;
using TornadoEos.Core.Logging;
using TornadoEos.Core.Models;
using TornadoEos.Core.Services;
using TornadoEos.Hardware;

namespace TornadoEos.App.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private CameraService _service;
    private readonly Dispatcher _dispatcher;

    private bool _isConnected;
    private bool _isBusy;
    private bool _isDiagnosticsRunning;
    private string _connectionStatus = "未连接";
    private string _cameraModel = "—";
    private string _serialNumber = "—";
    private string _firmwareVersion = "—";
    private string _batteryText = "—";
    private string _portDescription = "—";
    private string _currentLanguageText = "—";
    private bool _languageLockEnabled;
    private bool _languageLockKnown;
    private bool _isDemoMode;
    private bool _isSwitchingBackend;
    private bool _autoUnlock = true;
    private CameraLanguage? _selectedLanguage;
    private int _progressValue;
    private string _progressText = string.Empty;
    private bool _isProgressVisible;
    private string _diagnosticsSummary = "注意：若当前已连接相机，运行诊断会先断开本次连接。诊断只读取 WPD、MTP 和 EOS 属性，不会修改相机。";
    private string _diagnosticsReport = string.Empty;

    public MainViewModel() : this(new CameraService(new TornadoEos.Hardware.WpdCameraBackend()))
    {
    }

    public MainViewModel(CameraService service)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _dispatcher = Dispatcher.CurrentDispatcher;
        _isDemoMode = _service.IsSimulation;

        foreach (var language in CameraLanguages.All)
            Languages.Add(language);
        _selectedLanguage = CameraLanguages.FindByIso("zh-CN");

        WireService(_service);

        ConnectCommand = new AsyncRelayCommand(ConnectAsync, () => !IsConnected && !IsBusy && !IsDiagnosticsRunning);
        DisconnectCommand = new AsyncRelayCommand(DisconnectAsync, () => IsConnected && !IsBusy && !IsDiagnosticsRunning);
        ApplyLanguageCommand = new AsyncRelayCommand(ApplyLanguageAsync, () => IsConnected && !IsBusy && !IsDiagnosticsRunning && SelectedLanguage is not null && SupportsMenuLanguageWrite);
        RunDiagnosticsCommand = new AsyncRelayCommand(RunDiagnosticsAsync, () => !IsBusy && !IsDiagnosticsRunning);
        CopyDiagnosticsCommand = new AsyncRelayCommand(CopyDiagnosticsAsync, () => HasDiagnosticsReport && !IsDiagnosticsRunning);
        SaveDiagnosticsCommand = new AsyncRelayCommand(SaveDiagnosticsAsync, () => HasDiagnosticsReport && !IsDiagnosticsRunning);

        Log(LogLevel.Info, "Tornado EOS 已就绪，请连接相机开始使用。");
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

    public bool SupportsMenuLanguageWrite => _service.SupportsMenuLanguageWrite;

    public bool IsDemoMode
    {
        get => _isDemoMode;
        set
        {
            if (value == _isDemoMode)
                return;
            if (!CanSwitchBackend)
            {
                OnPropertyChanged();
                return;
            }

            _ = SwitchBackendAsync(value);
        }
    }

    public bool CanSwitchBackend => !IsBusy && !IsDiagnosticsRunning && !_isSwitchingBackend;

    public string LanguageCapabilityNotice => IsDemoMode
        ? "演示模式：可安全演示改语言，不接触真机。"
        : "当前为真机只读连接，无法修改菜单语言。";

    public string CameraEmptyStateText => IsDemoMode
        ? "演示模式无需连接真机，点击「连接相机」即可体验模拟 EOS R50。"
        : "请用 USB 连接相机并确认已开机";

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
        private set
        {
            if (SetProperty(ref _languageLockEnabled, value))
                OnPropertyChanged(nameof(LanguageLockText));
        }
    }

    public bool LanguageLockKnown
    {
        get => _languageLockKnown;
        private set
        {
            if (SetProperty(ref _languageLockKnown, value))
                OnPropertyChanged(nameof(LanguageLockText));
        }
    }

    public string LanguageLockText => !LanguageLockKnown
        ? "—"
        : LanguageLockEnabled
            ? "已启用（仅英语、日语）"
            : "已关闭（全部语言可见）";

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
            BatteryText = info.BatteryPercent >= 0 ? $"{info.BatteryPercent}%" : "无法读取";
            PortDescription = info.PortDescription;
            LanguageLockEnabled = _service.IsLanguageLockEnabled;
            LanguageLockKnown = true;
        }
        catch (Exception ex)
        {
            Log(LogLevel.Error, $"连接失败：{ex.Message}");
        }
    }

    private async Task DisconnectAsync()
    {
        try
        {
            await _service.DisconnectAsync();
            ResetCameraInfo();
        }
        catch (Exception ex)
        {
            Log(LogLevel.Error, $"断开连接失败：{ex.Message}");
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
            LanguageLockKnown = true;
        }
        catch (Exception ex)
        {
            Log(LogLevel.Error, $"设置语言失败：{ex.Message}");
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
            Log(LogLevel.Info, "正在启动只读相机诊断（WPD + MTP + EOS）……");

            if (IsConnected)
            {
                Log(LogLevel.Info, "将先断开当前连接，以便诊断程序独占访问 USB 会话。");
                await _service.DisconnectAsync().ConfigureAwait(true);
                ResetCameraInfo();
            }

            var result = await Task.Run(() => CameraDiagnostics.Run(msg =>
                Log(LogLevel.Debug, msg))).ConfigureAwait(true);

            DiagnosticsReport = result.ReportText;
            DiagnosticsSummary = result.SummaryText;
            Log(LogLevel.Info, $"诊断完成。{result.SummaryText}");
        }
        catch (Exception ex)
        {
            Log(LogLevel.Error, $"诊断失败：{ex.Message}");
            DiagnosticsSummary = "诊断失败，请查看操作日志。";
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
            Log(LogLevel.Info, "诊断报告已复制到剪贴板。");
        }
        return Task.CompletedTask;
    }

    private Task SaveDiagnosticsAsync()
    {
        if (!HasDiagnosticsReport)
            return Task.CompletedTask;

        var dialog = new SaveFileDialog
        {
            Title = "保存诊断报告",
            Filter = "文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*",
            FileName = $"tornado-eos-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.txt",
            DefaultExt = ".txt",
        };

        if (dialog.ShowDialog() != true)
            return Task.CompletedTask;

        File.WriteAllText(dialog.FileName, DiagnosticsReport);
        Log(LogLevel.Info, $"诊断报告已保存到：{dialog.FileName}");
        return Task.CompletedTask;
    }

    private void OnStateChanged(object? sender, ConnectionState state) => OnUi(() =>
    {
        ConnectionStatus = state switch
        {
            ConnectionState.Disconnected => "未连接",
            ConnectionState.Connecting => "正在连接…",
            ConnectionState.Connected => "已连接",
            ConnectionState.ServiceMode => "服务模式",
            ConnectionState.Busy => "正在处理…",
            ConnectionState.Error => "发生错误",
            _ => state.ToString(),
        };
        IsConnected = state is ConnectionState.Connected or ConnectionState.ServiceMode or ConnectionState.Busy;
        IsBusy = state is ConnectionState.Connecting or ConnectionState.Busy;
        if (state == ConnectionState.Disconnected)
            ResetCameraInfo();
    });

    private async Task SwitchBackendAsync(bool useDemoMode)
    {
        _isSwitchingBackend = true;
        OnPropertyChanged(nameof(CanSwitchBackend));
        RaiseCommandStates();

        try
        {
            if (IsConnected)
            {
                await _service.DisconnectAsync().ConfigureAwait(true);
                ResetCameraInfo();
            }

            var oldService = _service;
            UnwireService(oldService);
            _service = new CameraService(useDemoMode
                ? new SimulatedCameraBackend()
                : new WpdCameraBackend());
            WireService(_service);
            oldService.Dispose();

            _isDemoMode = useDemoMode;
            OnPropertyChanged(nameof(IsDemoMode));
            OnPropertyChanged(nameof(SupportsMenuLanguageWrite));
            OnPropertyChanged(nameof(LanguageCapabilityNotice));
            OnPropertyChanged(nameof(CameraEmptyStateText));
            Log(LogLevel.Info, useDemoMode
                ? "已切换到演示模式（模拟 EOS R50，不接触真机）。"
                : "已切换到真机只读连接（WPD）。请用 USB 连接相机。");
        }
        catch (Exception ex)
        {
            OnPropertyChanged(nameof(IsDemoMode));
            Log(LogLevel.Error, $"切换后端失败：{ex.Message}");
        }
        finally
        {
            _isSwitchingBackend = false;
            OnPropertyChanged(nameof(CanSwitchBackend));
            RaiseCommandStates();
        }
    }

    private void ResetCameraInfo()
    {
        CameraModel = SerialNumber = FirmwareVersion = BatteryText = PortDescription = "—";
        CurrentLanguageText = "—";
        LanguageLockEnabled = false;
        LanguageLockKnown = false;
    }

    private void WireService(CameraService service)
    {
        service.LogReceived += OnLog;
        service.StateChanged += OnStateChanged;
        service.ProgressChanged += OnProgress;
        service.CurrentLanguageChanged += OnCurrentLanguageChanged;
    }

    private void UnwireService(CameraService service)
    {
        service.LogReceived -= OnLog;
        service.StateChanged -= OnStateChanged;
        service.ProgressChanged -= OnProgress;
        service.CurrentLanguageChanged -= OnCurrentLanguageChanged;
    }

    private void OnProgress(object? sender, ServiceProgress p) => OnUi(() =>
    {
        ProgressValue = p.Percent;
        ProgressText = p.Message;
    });

    private void OnCurrentLanguageChanged(object? sender, CameraLanguage language) => OnUi(() =>
        CurrentLanguageText = $"{language.ChineseName}（{language.NativeName}）");

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
        OnPropertyChanged(nameof(CanSwitchBackend));
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
        UnwireService(_service);
        _service.Dispose();
    }
}
