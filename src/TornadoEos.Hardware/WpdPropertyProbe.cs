using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace TornadoEos.Hardware;

/// <summary>A single property read from a portable device's root (DEVICE) object.</summary>
public sealed record WpdPropertyEntry(Guid Category, uint Pid, string? KnownName, string TypeName, string Value)
{
    /// <summary>True for properties not part of the documented WPD device schema
    /// (i.e. vendor / MTP-specific codes worth investigating).</summary>
    public bool IsVendorOrUnknown => KnownName is null;

    public string KeyText => $"{{{Category}}} / {Pid} (0x{Pid:X4})";
}

/// <summary>Result of probing one connected portable device.</summary>
public sealed record WpdProbeDevice(
    string DeviceId,
    string? Manufacturer,
    string? Model,
    string? FriendlyName,
    bool IsCanon,
    IReadOnlyList<WpdPropertyEntry> Properties);

/// <summary>
/// Read-only diagnostic that enumerates every Windows Portable Device and dumps the
/// full property set of each device's root object (model, serial, firmware, plus any
/// vendor / MTP-specific properties Windows surfaces).
///
/// This NEVER writes anything and NEVER enters service mode — it is completely safe.
/// It is the first, safe step of investigating whether a camera exposes any
/// language/region related field over the standard USB (PTP/MTP → WPD) interface.
/// Deeper raw vendor PTP opcodes are NOT accessed here (that would require a
/// different, riskier driver path).
/// </summary>
[SupportedOSPlatform("windows")]
public static class WpdPropertyProbe
{
    // WPD property categories we can name.
    private static readonly Guid DeviceCategory = new("26D4979A-E643-4626-9E2B-736DC0C92FDC");
    private static readonly Guid ObjectCategory = new("EF6B490D-5CD8-437A-AFFC-DA8B60EE4A3C");

    private static readonly Dictionary<(Guid, uint), string> KnownKeys = new()
    {
        [(DeviceCategory, 2)] = "WPD_DEVICE_SYNC_PARTNER",
        [(DeviceCategory, 3)] = "WPD_DEVICE_FIRMWARE_VERSION",
        [(DeviceCategory, 4)] = "WPD_DEVICE_POWER_LEVEL",
        [(DeviceCategory, 5)] = "WPD_DEVICE_POWER_SOURCE",
        [(DeviceCategory, 6)] = "WPD_DEVICE_PROTOCOL",
        [(DeviceCategory, 7)] = "WPD_DEVICE_MANUFACTURER",
        [(DeviceCategory, 8)] = "WPD_DEVICE_MODEL",
        [(DeviceCategory, 9)] = "WPD_DEVICE_SERIAL_NUMBER",
        [(DeviceCategory, 10)] = "WPD_DEVICE_SUPPORTS_NON_CONSUMABLE",
        [(DeviceCategory, 11)] = "WPD_DEVICE_DATETIME",
        [(DeviceCategory, 12)] = "WPD_DEVICE_FRIENDLY_NAME",
        [(DeviceCategory, 15)] = "WPD_DEVICE_TYPE",
        [(DeviceCategory, 16)] = "WPD_DEVICE_NETWORK_IDENTIFIER",
        [(DeviceCategory, 17)] = "WPD_DEVICE_FUNCTIONAL_UNIQUE_ID",
        [(DeviceCategory, 18)] = "WPD_DEVICE_MODEL_UNIQUE_ID",
        [(DeviceCategory, 19)] = "WPD_DEVICE_TRANSPORT",
        [(DeviceCategory, 20)] = "WPD_DEVICE_USE_DEVICE_STAGE",
        [(ObjectCategory, 2)] = "WPD_OBJECT_ID",
        [(ObjectCategory, 3)] = "WPD_OBJECT_PARENT_ID",
        [(ObjectCategory, 4)] = "WPD_OBJECT_NAME",
        [(ObjectCategory, 5)] = "WPD_OBJECT_PERSISTENT_UNIQUE_ID",
        [(ObjectCategory, 6)] = "WPD_OBJECT_FORMAT",
        [(ObjectCategory, 7)] = "WPD_OBJECT_CONTENT_TYPE",
    };

    /// <summary>Probes all connected portable devices. Optionally reports progress via <paramref name="log"/>.</summary>
    public static IReadOnlyList<WpdProbeDevice> Run(Action<string>? log = null)
    {
        log?.Invoke("正在枚举 USB 便携设备（WPD）……");
        var manager = (IPortableDeviceManager)Wpd.CreateInstance(Wpd.CLSID_PortableDeviceManager);
        uint count = 0;
        manager.GetDevices(null, ref count);
        if (count == 0)
        {
            log?.Invoke("未找到便携式或 USB 设备。请连接并开启相机后重试。");
            return Array.Empty<WpdProbeDevice>();
        }

        var ids = new string[count];
        manager.GetDevices(ids, ref count);
        log?.Invoke($"找到 {count} 个便携设备，正在只读读取属性……");

        var results = new List<WpdProbeDevice>();
        foreach (var id in ids)
        {
            try
            {
                results.Add(ProbeDevice(id, log));
            }
            catch (Exception ex)
            {
                log?.Invoke($"  已跳过一个设备：{ex.Message}");
            }
        }
        return results;
    }

    private static WpdProbeDevice ProbeDevice(string id, Action<string>? log)
    {
        IPortableDevice? device = null;
        try
        {
            device = (IPortableDevice)Wpd.CreateInstance(Wpd.CLSID_PortableDevice);
            var clientInfo = (IPortableDeviceValues)Wpd.CreateInstance(Wpd.CLSID_PortableDeviceValues);
            Step(log, "Open", () => device!.Open(id, clientInfo));

            IPortableDeviceContent content = null!;
            Step(log, "Content", () => device!.Content(out content));
            IPortableDeviceProperties props = null!;
            Step(log, "Properties", () => content.Properties(out props));

            // Canon's WPD provider rejects index-based GetAt on the values collection,
            // so we enumerate the supported KEYS and read each value by key instead.
            IPortableDeviceKeyCollection keys = null!;
            Step(log, "GetSupportedProperties", () => props.GetSupportedProperties(Wpd.DeviceObjectId, out keys));
            IPortableDeviceValues values = null!;
            Step(log, "GetValues", () => props.GetValues(Wpd.DeviceObjectId, keys, out values));

            uint kn = 0;
            keys.GetCount(out kn);

            var entries = new List<WpdPropertyEntry>();
            for (uint j = 0; j < kn; j++)
            {
                keys.GetAt(j, out var key);
                var (typeName, value) = ReadValue(values, key);
                KnownKeys.TryGetValue((key.fmtid, key.pid), out var name);
                entries.Add(new WpdPropertyEntry(key.fmtid, key.pid, name, typeName, value));
            }

            string? manufacturer = Find(entries, DeviceCategory, 7);
            string? model = Find(entries, DeviceCategory, 8);
            string? friendly = Find(entries, DeviceCategory, 12);
            bool isCanon = $"{manufacturer} {model} {friendly}".ToLowerInvariant().Contains("canon");

            log?.Invoke($"  {(isCanon ? "[Canon] " : "")}{model ?? friendly ?? id}：{entries.Count} 个属性。");
            return new WpdProbeDevice(id, manufacturer, model, friendly, isCanon, entries);
        }
        finally
        {
            if (device is not null)
            {
                try { device.Close(); } catch { /* ignore */ }
                try { Marshal.ReleaseComObject(device); } catch { /* ignore */ }
            }
        }
    }

    /// <summary>Reads one property value by key using a typed-getter cascade
    /// (string → uint → generic PROPVARIANT), each guarded.</summary>
    private static (string TypeName, string Value) ReadValue(IPortableDeviceValues values, PROPERTYKEY keyTemplate)
    {
        var key = keyTemplate;
        try
        {
            values.GetStringValue(ref key, out string s);
            return ("STRING", s ?? "");
        }
        catch (COMException) { }

        key = keyTemplate;
        try
        {
            values.GetUnsignedIntegerValue(ref key, out uint u);
            return ("UINT", $"{u} (0x{u:X})");
        }
        catch (COMException) { }

        key = keyTemplate;
        try
        {
            values.GetValue(ref key, out PROPVARIANT pv);
            try { return (TypeName(pv.vt), FormatValue(ref pv)); }
            finally { NativeMethods.PropVariantClear(ref pv); }
        }
        catch (COMException ex)
        {
            return ("?", $"(unreadable: 0x{(uint)ex.HResult:X8})");
        }
    }

    private static void Step(Action<string>? log, string name, Action action)
    {
        try { action(); }
        catch (Exception ex) { throw new InvalidOperationException($"step '{name}' failed: {Hr(ex)}", ex); }
    }

    private static string Hr(Exception ex) =>
        ex is COMException c ? $"0x{(uint)c.HResult:X8} {c.Message.Trim()}" : ex.Message.Trim();

    private static string? Find(List<WpdPropertyEntry> entries, Guid cat, uint pid)
    {
        foreach (var e in entries)
            if (e.Category == cat && e.Pid == pid)
                return e.Value;
        return null;
    }

    private static string TypeName(ushort vt) => vt switch
    {
        0 => "EMPTY",
        2 => "I2",
        3 => "I4",
        8 => "BSTR",
        11 => "BOOL",
        17 => "UI1",
        18 => "UI2",
        19 => "UI4",
        20 => "I8",
        21 => "UI8",
        30 => "LPSTR",
        31 => "LPWSTR",
        72 => "CLSID",
        _ => $"VT_{vt}",
    };

    private static string FormatValue(ref PROPVARIANT pv)
    {
        switch (pv.vt)
        {
            case 31: return Marshal.PtrToStringUni(pv.pointerValue) ?? "";
            case 30: return Marshal.PtrToStringAnsi(pv.pointerValue) ?? "";
            case 8: return Marshal.PtrToStringBSTR(pv.pointerValue) ?? "";
            case 19: return $"{pv.uintVal} (0x{pv.uintVal:X})";
            case 3: return pv.intVal.ToString(CultureInfo.InvariantCulture);
            case 21: return $"{pv.ulongVal} (0x{pv.ulongVal:X})";
            case 20: return pv.longVal.ToString(CultureInfo.InvariantCulture);
            case 18: return $"{pv.uiVal} (0x{pv.uiVal:X})";
            case 2: return pv.iVal.ToString(CultureInfo.InvariantCulture);
            case 17: return $"{pv.bVal} (0x{pv.bVal:X2})";
            case 11: return pv.boolVal != 0 ? "true" : "false";
            case 72:
                try { return Marshal.PtrToStructure<Guid>(pv.pointerValue).ToString("B"); }
                catch { return "(clsid)"; }
            case 0: return "(empty)";
            default: return $"(unhandled VT_{pv.vt})";
        }
    }
}
