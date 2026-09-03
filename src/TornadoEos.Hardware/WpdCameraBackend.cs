using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using TornadoEos.Core.Abstractions;
using TornadoEos.Core.Logging;
using TornadoEos.Core.Models;

namespace TornadoEos.Hardware;

/// <summary>
/// Real hardware backend that connects to a Canon camera over USB using the
/// Windows Portable Devices (WPD) API and reads genuine device data
/// (model, serial number, firmware version, battery level).
///
/// WPD can read these device properties for real, but it CANNOT enter Canon's
/// service mode or write the firmware menu-language property — that requires the
/// undocumented service-mode protocol. Those operations therefore throw
/// <see cref="NotSupportedException"/> here, while connect + read are fully real.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WpdCameraBackend : ICameraBackend
{
    private readonly object _gate = new();
    private IPortableDevice? _device;
    private bool _connected;

    public string Name => "佳能相机（WPD / USB）";

    public bool SupportsMenuLanguageWrite => false;

    public bool IsSimulation => false;

    public bool IsConnected { get { lock (_gate) return _connected; } }

    public bool IsInServiceMode => false;

    public event EventHandler<LogEntry>? Log;

    public Task<CameraInfo> ConnectAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => ConnectCore(cancellationToken), cancellationToken);

    private CameraInfo ConnectCore(CancellationToken cancellationToken)
    {
        EmitLog(LogLevel.Info, "正在枚举 USB 便携设备（WPD）……");

        var manager = (IPortableDeviceManager)Wpd.CreateInstance(Wpd.CLSID_PortableDeviceManager);
        uint count = 0;
        manager.GetDevices(null, ref count);
        if (count == 0)
            throw new InvalidOperationException("未找到便携式或 USB 图像设备。请通过 USB 连接相机并确认相机已开机。");

        var deviceIds = new string[count];
        manager.GetDevices(deviceIds, ref count);
        EmitLog(LogLevel.Debug, $"找到 {count} 个便携设备，正在查找佳能相机……");

        foreach (var id in deviceIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IPortableDevice? device = null;
            try
            {
                device = (IPortableDevice)Wpd.CreateInstance(Wpd.CLSID_PortableDevice);
                var clientInfo = (IPortableDeviceValues)Wpd.CreateInstance(Wpd.CLSID_PortableDeviceValues);
                device.Open(id, clientInfo);

                device.Content(out var content);
                content.Properties(out var props);
                props.GetSupportedProperties(Wpd.DeviceObjectId, out var keys);
                props.GetValues(Wpd.DeviceObjectId, keys, out var values);

                string? manufacturer = ReadString(values, Wpd.Manufacturer);
                string? model = ReadString(values, Wpd.Model);
                string? friendly = ReadString(values, Wpd.FriendlyName);

                if (!IsCanonCamera(manufacturer, model, friendly))
                {
                    SafeClose(device);
                    device = null;
                    continue;
                }

                string serial = ReadString(values, Wpd.SerialNumber) ?? "（未知）";
                string firmware = ReadString(values, Wpd.FirmwareVersion) ?? "（未知）";
                int battery = ReadUInt(values, Wpd.PowerLevel) is uint p ? (int)p : -1;
                string displayModel = model ?? friendly ?? "佳能相机";

                lock (_gate)
                {
                    _device = device;
                    _connected = true;
                }
                device = null; // ownership transferred

                EmitLog(LogLevel.Success, $"已连接 {displayModel}，序列号 {serial}，固件 {firmware}。");
                EmitLog(LogLevel.Warning,
                    "注意：WPD 不提供菜单语言读写能力；当前连接只能读取真实设备信息。");

                return new CameraInfo(displayModel, serial, firmware, battery, "USB (WPD)");
            }
            catch (Exception ex)
            {
                EmitLog(LogLevel.Debug, $"已跳过一个设备（{ex.Message}）。");
            }
            finally
            {
                if (device is not null)
                    SafeClose(device);
            }
        }

        throw new InvalidOperationException(
            "已连接的 USB 设备中没有检测到佳能相机。请确认相机已开机、通过 USB 连接，且未被其他程序占用。");
    }

    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        IPortableDevice? device;
        lock (_gate)
        {
            device = _device;
            _device = null;
            _connected = false;
        }
        if (device is not null)
            SafeClose(device);
        EmitLog(LogLevel.Info, "相机连接已断开。");
        return Task.CompletedTask;
    }

    public Task EnterServiceModeAsync(CancellationToken cancellationToken = default) => throw Unsupported();

    public Task<IReadOnlyList<CameraLanguage>> GetAvailableLanguagesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<CameraLanguage>>(CameraLanguages.All.ToList());

    public Task<CameraLanguage> GetCurrentLanguageAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(CameraLanguages.Unknown);

    public Task SetMenuLanguageAsync(CameraLanguage language, IProgress<ServiceProgress>? progress = null, CancellationToken cancellationToken = default) =>
        throw Unsupported();

    public Task<bool> GetLanguageLockAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(false);

    public Task SetLanguageLockAsync(bool enabled, IProgress<ServiceProgress>? progress = null, CancellationToken cancellationToken = default) =>
        throw Unsupported();

    public void Dispose()
    {
        IPortableDevice? device;
        lock (_gate)
        {
            device = _device;
            _device = null;
            _connected = false;
        }
        if (device is not null)
            SafeClose(device);
    }

    private static NotSupportedException Unsupported() => new(
        "WPD 无法设置或解锁相机菜单语言。该功能需要佳能未公开的服务模式协议；" +
        "公开 EDSDK 不提供此能力，EOS R50 的相关协议也没有公开资料。");

    private static bool IsCanonCamera(string? manufacturer, string? model, string? friendly)
    {
        string blob = $"{manufacturer} {model} {friendly}".ToLowerInvariant();
        bool isCanon = blob.Contains("canon");
        bool looksLikeCamera = blob.Contains("eos") || blob.Contains("powershot") || blob.Contains("camera") || blob.Contains("r50");
        return isCanon || looksLikeCamera;
    }

    private static string? ReadString(IPortableDeviceValues values, PROPERTYKEY keyTemplate)
    {
        var key = keyTemplate;
        try
        {
            values.GetStringValue(ref key, out string value);
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        catch (COMException)
        {
            return null;
        }
    }

    private static uint? ReadUInt(IPortableDeviceValues values, PROPERTYKEY keyTemplate)
    {
        var key = keyTemplate;
        try
        {
            values.GetUnsignedIntegerValue(ref key, out uint value);
            return value;
        }
        catch (COMException)
        {
            return null;
        }
    }

    private static void SafeClose(IPortableDevice device)
    {
        try { device.Close(); } catch { /* ignore */ }
        try { Marshal.ReleaseComObject(device); } catch { /* ignore */ }
    }

    private void EmitLog(LogLevel level, string message) =>
        Log?.Invoke(this, LogEntry.Now(level, message));
}
