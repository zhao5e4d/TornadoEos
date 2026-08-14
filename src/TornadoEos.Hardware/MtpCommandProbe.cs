using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace TornadoEos.Hardware;

/// <summary>Outcome of the read-only MTP capability probe.</summary>
public sealed record MtpCapabilities(
    string DeviceId,
    string? Model,
    string? VendorExtensionDescription,
    IReadOnlyList<ushort> VendorOperationCodes,
    string? Error);

/// <summary>
/// Read-only probe that uses the WPD MTP pass-through (IPortableDevice.SendCommand)
/// to ask the driver two safe questions about the connected camera:
///
///   1. WPD_COMMAND_MTP_EXT_GET_VENDOR_EXTENSION_DESCRIPTION — the MTP vendor
///      extension string declared in the device's DeviceInfo dataset.
///   2. WPD_COMMAND_MTP_EXT_GET_SUPPORTED_VENDOR_OPCODES — the list of
///      vendor-extended operation codes (e.g. Canon 0x9xxx) the device exposes.
///
/// Both commands are queries with no data phase and DO NOT modify the camera.
/// They reveal whether any Canon vendor operations are reachable over this USB
/// channel — the prerequisite for anything like a service-mode language unlock.
/// </summary>
[SupportedOSPlatform("windows")]
public static class MtpCommandProbe
{
    private static readonly Guid CommonCategory = new("F0422A9C-5DC8-4440-B5BD-5DF28835658A");
    private static readonly Guid MtpExtCategory = new("4D545058-1A2E-4106-A357-771E0819FC56");

    private static PROPERTYKEY CommonCommandCategory => new(CommonCategory, 1001);
    private static PROPERTYKEY CommonCommandId => new(CommonCategory, 1002);

    private const uint CmdGetSupportedVendorOpcodes = 11;
    private const uint CmdGetVendorExtensionDescription = 18;

    private static PROPERTYKEY PropVendorOperationCodes => new(MtpExtCategory, 1005);
    private static PROPERTYKEY PropVendorExtensionDescription => new(MtpExtCategory, 1014);

    public static IReadOnlyList<MtpCapabilities> Run(Action<string>? log = null)
    {
        var devices = WpdPropertyProbe.Run(log);
        var results = new List<MtpCapabilities>();
        foreach (var dev in devices)
        {
            if (!dev.IsCanon)
                continue;
            results.Add(ProbeCore(dev.DeviceId, dev.Model, log));
        }
        return results;
    }

    /// <summary>Probes one device by ID (does not re-enumerate).</summary>
    public static MtpCapabilities ProbeDevice(string deviceId, string? model, Action<string>? log = null) =>
        ProbeCore(deviceId, model, log);

    private static MtpCapabilities ProbeCore(string deviceId, string? model, Action<string>? log)
    {
        IPortableDevice? device = null;
        try
        {
            device = (IPortableDevice)Wpd.CreateInstance(Wpd.CLSID_PortableDevice);
            var clientInfo = (IPortableDeviceValues)Wpd.CreateInstance(Wpd.CLSID_PortableDeviceValues);
            device.Open(deviceId, clientInfo);

            log?.Invoke("正在发送 MTP 查询：GET_VENDOR_EXTENSION_DESCRIPTION（只读）……");
            string? description = TryGetDescription(device);

            log?.Invoke("正在发送 MTP 查询：GET_SUPPORTED_VENDOR_OPCODES（只读）……");
            var opcodes = TryGetVendorOpcodes(device);

            return new MtpCapabilities(deviceId, model, description, opcodes, null);
        }
        catch (Exception ex)
        {
            string msg = ex is COMException c ? $"0x{(uint)c.HResult:X8} {c.Message.Trim()}" : ex.Message;
            return new MtpCapabilities(deviceId, model, null, Array.Empty<ushort>(), msg);
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

    private static IPortableDeviceValues NewCommand(uint commandId)
    {
        var values = (IPortableDeviceValues)Wpd.CreateInstance(Wpd.CLSID_PortableDeviceValues);
        var catKey = CommonCommandCategory;
        var idKey = CommonCommandId;
        var category = MtpExtCategory;
        values.SetGuidValue(ref catKey, ref category);
        values.SetUnsignedIntegerValue(ref idKey, commandId);
        return values;
    }

    private static string? TryGetDescription(IPortableDevice device)
    {
        try
        {
            var cmd = NewCommand(CmdGetVendorExtensionDescription);
            device.SendCommand(0, cmd, out var results);
            var key = PropVendorExtensionDescription;
            results.GetStringValue(ref key, out string desc);
            return desc;
        }
        catch (COMException)
        {
            return null;
        }
    }

    private static IReadOnlyList<ushort> TryGetVendorOpcodes(IPortableDevice device)
    {
        var list = new List<ushort>();
        try
        {
            var cmd = NewCommand(CmdGetSupportedVendorOpcodes);
            device.SendCommand(0, cmd, out var results);

            var key = PropVendorOperationCodes;
            results.GetIPortableDevicePropVariantCollectionValue(ref key, out var coll);
            coll.GetCount(out uint n);
            for (uint i = 0; i < n; i++)
            {
                coll.GetAt(i, out PROPVARIANT pv);
                try
                {
                    // Codes come back as VT_UI4.
                    list.Add((ushort)pv.uintVal);
                }
                finally { NativeMethods.PropVariantClear(ref pv); }
            }
        }
        catch (COMException)
        {
            // Device exposes no vendor opcodes through WPD, or query unsupported.
        }
        return list;
    }
}
