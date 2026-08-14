using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace TornadoEos.Hardware;

/// <summary>
/// Thin wrapper over the WPD MTP pass-through (IPortableDevice.SendCommand) that
/// implements the two phases this project needs:
///   - a command with no data phase (with operation parameters), and
///   - a command followed by a data-read phase (device → host).
///
/// All methods here are generic MTP plumbing; they perform exactly the operation
/// code they are told to. Callers are responsible for only issuing safe (read-only
/// or reversible) operations.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class MtpTransport
{
    private static readonly Guid CommonCategory = new("F0422A9C-5DC8-4440-B5BD-5DF28835658A");
    private static readonly Guid MtpExt = new("4D545058-1A2E-4106-A357-771E0819FC56");

    private const uint CmdExecNoData = 12;
    private const uint CmdExecReadData = 13;
    private const uint CmdReadData = 15;
    private const uint CmdEndTransfer = 17;

    private const uint PidOperationCode = 1001;
    private const uint PidOperationParams = 1002;
    private const uint PidResponseCode = 1003;
    private const uint PidTransferContext = 1006;
    private const uint PidTransferTotalDataSize = 1007;
    private const uint PidTransferNumBytesToRead = 1008;
    private const uint PidTransferNumBytesRead = 1009;
    private const uint PidTransferData = 1012;

    private readonly IPortableDevice _device;
    private readonly Action<string>? _log;

    public MtpTransport(IPortableDevice device, Action<string>? log = null)
    {
        _device = device;
        _log = log;
    }

    /// <summary>Issues an MTP operation with no data phase. Returns the response code.</summary>
    public uint ExecNoData(ushort opcode, uint[] parameters)
    {
        var cmd = NewCommand(CmdExecNoData, opcode, parameters);
        _device.SendCommand(0, cmd, out var results);
        uint hr = ReadCommonHResult(results);
        uint rc = TryReadUInt(results, PidResponseCode);
        _log?.Invoke($"    op 0x{opcode:X4} (no-data): common HRESULT=0x{hr:X8}, MTP response=0x{rc:X4}");
        return rc;
    }

    /// <summary>Issues an MTP operation followed by a device→host data phase. Returns the data.</summary>
    public byte[] ExecReadData(ushort opcode, uint[] parameters)
    {
        var cmd = NewCommand(CmdExecReadData, opcode, parameters);
        _device.SendCommand(0, cmd, out var results);

        uint hr = ReadCommonHResult(results);
        string context = TryReadString(results, PidTransferContext);
        ulong total = TryReadULong(results, PidTransferTotalDataSize);
        _log?.Invoke($"    op 0x{opcode:X4} (read): common HRESULT=0x{hr:X8}, total={total}, context='{(string.IsNullOrEmpty(context) ? "<none>" : "ok")}'");
        if (string.IsNullOrEmpty(context))
            return Array.Empty<byte>();

        var buffer = new List<byte>();
        uint chunk = total is > 0 and < 0x4000000 ? (uint)total : 0x100000;
        if (chunk == 0) chunk = 0x100000;

        for (int guard = 0; guard < 4096; guard++)
        {
            var read = NewExt(CmdReadData);
            SetString(read, PidTransferContext, context);
            SetUInt(read, PidTransferNumBytesToRead, chunk);
            // TRANSFER_DATA is in/out: the driver copies into this pre-allocated buffer.
            SetBuffer(read, PidTransferData, (int)chunk);
            _device.SendCommand(0, read, out var rres);

            uint got = TryReadUInt(rres, PidTransferNumBytesRead);
            byte[]? part = TryReadBuffer(rres, PidTransferData);
            if (got > 0 && part is { Length: > 0 })
            {
                int take = Math.Min((int)got, part.Length);
                for (int b = 0; b < take; b++)
                    buffer.Add(part[b]);
            }

            bool done =
                got == 0 ||
                (total > 0 && (ulong)buffer.Count >= total) ||
                (total == 0 && got < chunk);
            if (done)
                break;
        }

        var end = NewExt(CmdEndTransfer);
        SetString(end, PidTransferContext, context);
        try { _device.SendCommand(0, end, out _); } catch { /* ignore */ }

        return buffer.ToArray();
    }

    private IPortableDeviceValues NewExt(uint commandId)
    {
        var v = (IPortableDeviceValues)Wpd.CreateInstance(Wpd.CLSID_PortableDeviceValues);
        var catKey = Key(CommonCategory, 1001);
        var cat = MtpExt;
        v.SetGuidValue(ref catKey, ref cat);
        var idKey = Key(CommonCategory, 1002);
        v.SetUnsignedIntegerValue(ref idKey, commandId);
        return v;
    }

    private IPortableDeviceValues NewCommand(uint commandId, ushort opcode, uint[] parameters)
    {
        var v = NewExt(commandId);
        SetUInt(v, PidOperationCode, opcode);

        var coll = (IPortableDevicePropVariantCollection)Wpd.CreateInstance(Wpd.CLSID_PortableDevicePropVariantCollection);
        foreach (var p in parameters)
        {
            var pv = new PROPVARIANT { vt = 19, uintVal = p }; // VT_UI4
            coll.Add(ref pv);
        }
        var paramsKey = Key(MtpExt, PidOperationParams);
        v.SetIPortableDevicePropVariantCollectionValue(ref paramsKey, coll);
        return v;
    }

    private static void SetUInt(IPortableDeviceValues v, uint pid, uint value)
    {
        var k = Key(MtpExt, pid);
        v.SetUnsignedIntegerValue(ref k, value);
    }

    private static void SetString(IPortableDeviceValues v, uint pid, string value)
    {
        var k = Key(MtpExt, pid);
        v.SetStringValue(ref k, value);
    }

    private static void SetBuffer(IPortableDeviceValues v, uint pid, int size)
    {
        var k = Key(MtpExt, pid);
        IntPtr p = Marshal.AllocCoTaskMem(size);
        try
        {
            // SetBufferValue copies the bytes into the values store synchronously,
            // so the temporary buffer can be freed immediately afterwards.
            v.SetBufferValue(ref k, p, (uint)size);
        }
        finally { Marshal.FreeCoTaskMem(p); }
    }

    private static uint TryReadUInt(IPortableDeviceValues v, uint pid)
    {
        var k = Key(MtpExt, pid);
        try { v.GetUnsignedIntegerValue(ref k, out uint val); return val; }
        catch (COMException) { return 0; }
    }

    /// <summary>Reads WPD_PROPERTY_COMMON_HRESULT (the per-command driver result).</summary>
    private static uint ReadCommonHResult(IPortableDeviceValues v)
    {
        var k = Key(CommonCategory, 1003);
        try
        {
            v.GetValue(ref k, out PROPVARIANT pv);
            try { return unchecked((uint)pv.intVal); }   // VT_ERROR stores the scode in the int slot
            finally { NativeMethods.PropVariantClear(ref pv); }
        }
        catch (COMException) { return 0xFFFFFFFF; }
    }

    private static ulong TryReadULong(IPortableDeviceValues v, uint pid)
    {
        var k = Key(MtpExt, pid);
        try
        {
            v.GetValue(ref k, out PROPVARIANT pv);
            try { return pv.vt == 21 ? pv.ulongVal : pv.uintVal; }
            finally { NativeMethods.PropVariantClear(ref pv); }
        }
        catch (COMException) { return 0; }
    }

    private static string TryReadString(IPortableDeviceValues v, uint pid)
    {
        var k = Key(MtpExt, pid);
        try { v.GetStringValue(ref k, out string s); return s ?? ""; }
        catch (COMException) { return ""; }
    }

    private static byte[]? TryReadBuffer(IPortableDeviceValues v, uint pid)
    {
        var k = Key(MtpExt, pid);
        try
        {
            v.GetBufferValue(ref k, out IntPtr ptr, out uint cb);
            if (ptr == IntPtr.Zero || cb == 0)
                return Array.Empty<byte>();
            var data = new byte[cb];
            Marshal.Copy(ptr, data, 0, (int)cb);
            Marshal.FreeCoTaskMem(ptr);
            return data;
        }
        catch (COMException) { return null; }
    }

    private static PROPERTYKEY Key(Guid fmtid, uint pid) => new(fmtid, pid);
}
