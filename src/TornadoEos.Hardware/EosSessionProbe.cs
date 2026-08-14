using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace TornadoEos.Hardware;

/// <summary>One Canon EOS device property discovered during the session probe.</summary>
public sealed record EosProperty(ushort Code, string? KnownName, string ValueText);

/// <summary>Result of the read-only Canon EOS session probe.</summary>
public sealed record EosSessionResult(
    string DeviceId,
    string? Model,
    IReadOnlyList<ushort> EventsSupported,
    IReadOnlyList<EosProperty> Properties,
    string? Error);

/// <summary>
/// Drives a connected Canon EOS body into read-only PC-remote mode (the same mode
/// EOS Utility / the EDSDK use) and enumerates the EOS device-property set plus the
/// current values, all over the WPD MTP pass-through.
///
/// Operations used (Canon EOS vendor opcodes):
///   0x9114 SetRemoteMode, 0x9115 SetEventMode (reversible mode changes),
///   0x9108 GetDeviceInfoEx (read), 0x9116 GetEvent (read).
///
/// It does NOT enter service mode and does NOT write any device property.
/// </summary>
[SupportedOSPlatform("windows")]
public static class EosSessionProbe
{
    private const ushort OcSetRemoteMode = 0x9114;
    private const ushort OcSetEventMode = 0x9115;
    private const ushort OcGetDeviceInfoEx = 0x9108;
    private const ushort OcGetEvent = 0x9116;

    private const uint EcPropValueChanged = 0xC189; // PTP_EC_CANON_EOS_PropValueChanged

    // Best-effort names for common Canon EOS device-property codes (libgphoto2).
    private static readonly Dictionary<ushort, string> KnownProps = new()
    {
        [0xD101] = "Aperture",
        [0xD102] = "ShutterSpeed",
        [0xD103] = "ISOSpeed",
        [0xD104] = "ExpCompensation",
        [0xD105] = "AutoExposureMode",
        [0xD106] = "DriveMode",
        [0xD107] = "MeteringMode",
        [0xD108] = "FocusMode",
        [0xD109] = "WhiteBalance",
        [0xD10A] = "ColorTemperature",
        [0xD10B] = "WhiteBalanceAdjustA",
        [0xD10C] = "WhiteBalanceAdjustB",
        [0xD111] = "Owner",
        [0xD114] = "ImageFormat",
        [0xD1D3] = "PictureStyle",
        [0xD15C] = "Artist",
        [0xD15D] = "Copyright",
    };

    public static IReadOnlyList<EosSessionResult> Run(Action<string>? log = null)
    {
        var results = new List<EosSessionResult>();
        foreach (var dev in WpdPropertyProbe.Run(log))
        {
            if (!dev.IsCanon)
                continue;
            results.Add(ProbeCore(dev.DeviceId, dev.Model, log));
        }
        return results;
    }

    /// <summary>Probes one device by ID (does not re-enumerate).</summary>
    public static EosSessionResult ProbeDevice(string deviceId, string? model, Action<string>? log = null) =>
        ProbeCore(deviceId, model, log);

    private static EosSessionResult ProbeCore(string deviceId, string? model, Action<string>? log)
    {
        IPortableDevice? device = null;
        try
        {
            device = (IPortableDevice)Wpd.CreateInstance(Wpd.CLSID_PortableDevice);
            var clientInfo = (IPortableDeviceValues)Wpd.CreateInstance(Wpd.CLSID_PortableDeviceValues);
            device.Open(deviceId, clientInfo);
            var mtp = new MtpTransport(device, log);

            byte[] di = Array.Empty<byte>();
            uint[] usedMode = { 1, 0x15 };
            foreach (var mode in usedMode)
            {
                log?.Invoke($"正在进入电脑遥控模式（SetRemoteMode={mode}）……");
                mtp.ExecNoData(OcSetRemoteMode, new uint[] { mode });
                mtp.ExecNoData(OcSetEventMode, new uint[] { 1 });

                log?.Invoke("正在读取 EOS 设备信息（GetDeviceInfoEx，只读）……");
                di = mtp.ExecReadData(OcGetDeviceInfoEx, Array.Empty<uint>());
                if (di.Length >= 12)
                    break;
                log?.Invoke($"  模式 {mode} 返回 {di.Length} 字节，正在尝试下一模式……");
            }

            var (events, props, _) = ParseDeviceInfoEx(di);
            log?.Invoke($"EOS 返回 {events.Count} 个事件、{props.Count} 个设备属性。");

            log?.Invoke("正在读取当前属性值（GetEvent，只读）……");
            var values = ReadCurrentValues(mtp, log);

            var propList = new List<EosProperty>();
            foreach (var code in props)
            {
                KnownProps.TryGetValue(code, out var name);
                string valueText = values.TryGetValue(code, out var raw) ? FormatRaw(raw) : "(not reported)";
                propList.Add(new EosProperty(code, name, valueText));
            }

            // Politely leave remote mode; ignore failures.
            try { mtp.ExecNoData(OcSetRemoteMode, new uint[] { 0 }); } catch { /* ignore */ }

            return new EosSessionResult(deviceId, model, events, propList, null);
        }
        catch (Exception ex)
        {
            string msg = ex is COMException c ? $"0x{(uint)c.HResult:X8} {c.Message.Trim()}" : ex.Message;
            return new EosSessionResult(deviceId, model, Array.Empty<ushort>(), Array.Empty<EosProperty>(), msg);
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

    private static Dictionary<ushort, byte[]> ReadCurrentValues(MtpTransport mtp, Action<string>? log)
    {
        var map = new Dictionary<ushort, byte[]>();
        // GetEvent may need to be drained a few times to receive all property values.
        for (int round = 0; round < 6; round++)
        {
            byte[] data;
            try { data = mtp.ExecReadData(OcGetEvent, Array.Empty<uint>()); }
            catch (COMException) { break; }
            if (data.Length < 8)
                break;
            int before = map.Count;
            ParseEvents(data, map);
            if (map.Count == before && round > 0)
                break;
        }
        return map;
    }

    private static (List<ushort> events, List<ushort> props, List<ushort> unk) ParseDeviceInfoEx(byte[] d)
    {
        var events = new List<ushort>();
        var props = new List<ushort>();
        var unk = new List<ushort>();
        if (d.Length < 8)
            return (events, props, unk);

        int pos = 4; // skip leading uint32 (struct version)
        ReadCodeArray(d, ref pos, events);
        ReadCodeArray(d, ref pos, props);
        ReadCodeArray(d, ref pos, unk);
        return (events, props, unk);
    }

    private static void ReadCodeArray(byte[] d, ref int pos, List<ushort> dest)
    {
        if (pos + 4 > d.Length) return;
        uint count = BitConverter.ToUInt32(d, pos); pos += 4;
        for (uint i = 0; i < count && pos + 4 <= d.Length; i++)
        {
            uint code = BitConverter.ToUInt32(d, pos); pos += 4;
            dest.Add((ushort)code);
        }
    }

    private static void ParseEvents(byte[] d, Dictionary<ushort, byte[]> map)
    {
        int pos = 0;
        while (pos + 8 <= d.Length)
        {
            uint size = BitConverter.ToUInt32(d, pos);
            uint type = BitConverter.ToUInt32(d, pos + 4);
            if (size < 8 || pos + size > d.Length)
                break;
            if (type == EcPropValueChanged && size >= 12)
            {
                ushort prop = (ushort)BitConverter.ToUInt32(d, pos + 8);
                int valLen = (int)size - 12;
                var val = new byte[Math.Max(0, valLen)];
                if (valLen > 0)
                    Array.Copy(d, pos + 12, val, 0, valLen);
                map[prop] = val;
            }
            pos += (int)size;
        }
    }

    private static string FormatRaw(byte[] raw)
    {
        if (raw.Length == 0) return "(empty)";
        if (raw.Length == 4) return $"0x{BitConverter.ToUInt32(raw, 0):X8} ({BitConverter.ToUInt32(raw, 0)})";
        if (raw.Length == 2) return $"0x{BitConverter.ToUInt16(raw, 0):X4} ({BitConverter.ToUInt16(raw, 0)})";
        if (raw.Length == 1) return $"0x{raw[0]:X2} ({raw[0]})";
        var hex = BitConverter.ToString(raw, 0, Math.Min(raw.Length, 24)).Replace('-', ' ');
        return raw.Length > 24 ? $"{hex} … ({raw.Length} bytes)" : $"{hex} ({raw.Length} bytes)";
    }
}
