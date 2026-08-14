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

    public string Name => "Canon camera over WPD (USB)";

    public bool IsConnected { get { lock (_gate) return _connected; } }

    public bool IsInServiceMode => false;

    public event EventHandler<LogEntry>? Log;

    public Task<CameraInfo> ConnectAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => ConnectCore(cancellationToken), cancellationToken);

    private CameraInfo ConnectCore(CancellationToken cancellationToken)
    {
        EmitLog(LogLevel.Info, "Enumerating USB portable devices (WPD)...");

        var manager = (IPortableDeviceManager)Wpd.CreateInstance(Wpd.CLSID_PortableDeviceManager);
        uint count = 0;
        manager.GetDevices(null, ref count);
        if (count == 0)
            throw new InvalidOperationException("No portable/USB imaging devices were found. Connect the camera via USB and ensure it is powered on.");

        var deviceIds = new string[count];
        manager.GetDevices(deviceIds, ref count);
        EmitLog(LogLevel.Debug, $"Found {count} portable device(s). Looking for a Canon camera...");

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

                string serial = ReadString(values, Wpd.SerialNumber) ?? "(unknown)";
                string firmware = ReadString(values, Wpd.FirmwareVersion) ?? "(unknown)";
                int battery = ReadUInt(values, Wpd.PowerLevel) is uint p ? (int)p : -1;
                string displayModel = model ?? friendly ?? "Canon camera";

                lock (_gate)
                {
                    _device = device;
                    _connected = true;
                }
                device = null; // ownership transferred

                EmitLog(LogLevel.Success, $"Connected to {displayModel}, S/N {serial}, FW {firmware}.");
                EmitLog(LogLevel.Warning,
                    "Note: menu-language read/write is not exposed over WPD. Connection shows real device data only.");

                return new CameraInfo(displayModel, serial, firmware, battery, "USB (WPD)");
            }
            catch (Exception ex)
            {
                EmitLog(LogLevel.Debug, $"Skipped a device ({ex.Message}).");
            }
            finally
            {
                if (device is not null)
                    SafeClose(device);
            }
        }

        throw new InvalidOperationException(
            "No Canon camera was detected among the connected USB devices. Make sure the camera is on, " +
            "connected via USB, and not busy in another program.");
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
        EmitLog(LogLevel.Info, "Camera disconnected.");
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
        "Setting/unlocking the camera menu language is not possible over WPD. It requires Canon's " +
        "undocumented service-mode protocol (not available in the public EDSDK and not published for the EOS R50).");

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
