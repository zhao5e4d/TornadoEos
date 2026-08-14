using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace TornadoEos.Hardware;

/// <summary>
/// Minimal hand-written COM interop for the Windows Portable Devices (WPD) API.
/// Only the members needed to enumerate devices and read device-level properties
/// are declared; unused vtable slots are kept (in order) so the slots we call land
/// at the correct offsets. Strings are read with GetStringValue / numbers with
/// GetUnsignedIntegerValue, which avoids any PROPVARIANT marshalling.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class Wpd
{
    public static readonly Guid CLSID_PortableDeviceManager = new("0AF10CEC-2ECD-4B92-9581-34F6AE0637F3");
    public static readonly Guid CLSID_PortableDevice = new("728A21C5-3D9E-48D7-9810-864848F0F404");
    public static readonly Guid CLSID_PortableDeviceValues = new("0C15D503-D017-47CE-9016-7B3F978721CC");
    public static readonly Guid CLSID_PortableDevicePropVariantCollection = new("08A99E2F-6D6D-4B80-AF5A-BAF2BCBE4CB9");

    /// <summary>Object id of the device root used when reading device properties.</summary>
    public const string DeviceObjectId = "DEVICE";

    // WPD_DEVICE_* properties live in this category.
    private static readonly Guid DeviceCategory = new("26D4979A-E643-4626-9E2B-736DC0C92FDC");

    public static PROPERTYKEY FirmwareVersion => new(DeviceCategory, 3);
    public static PROPERTYKEY PowerLevel => new(DeviceCategory, 4);
    public static PROPERTYKEY Manufacturer => new(DeviceCategory, 7);
    public static PROPERTYKEY Model => new(DeviceCategory, 8);
    public static PROPERTYKEY SerialNumber => new(DeviceCategory, 9);
    public static PROPERTYKEY FriendlyName => new(DeviceCategory, 12);

    public static object CreateInstance(Guid clsid) =>
        Activator.CreateInstance(Type.GetTypeFromCLSID(clsid)!)!;
}

[StructLayout(LayoutKind.Sequential)]
internal struct PROPERTYKEY
{
    public Guid fmtid;
    public uint pid;
    public PROPERTYKEY(Guid fmtid, uint pid) { this.fmtid = fmtid; this.pid = pid; }
}

[ComImport, Guid("A1567595-4C2F-4574-A6FA-ECEF917B9A40"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPortableDeviceManager
{
    void GetDevices(
        [In, Out, MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPWStr)] string[]? pPnPDeviceIDs,
        ref uint pcPnPDeviceIDs);
    void RefreshDeviceList();
    // Remaining members are unused; declared only to preserve vtable order.
    void GetDeviceFriendlyName([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr name, ref uint cch);
    void GetDeviceDescription([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr desc, ref uint cch);
    void GetDeviceManufacturer([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr man, ref uint cch);
}

[ComImport, Guid("625E2DF8-6392-4CF0-9AD1-3CFA5F17775C"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPortableDevice
{
    void Open([MarshalAs(UnmanagedType.LPWStr)] string pszPnPDeviceID, IPortableDeviceValues pClientInfo);
    void SendCommand(uint dwFlags, IPortableDeviceValues pParameters, out IPortableDeviceValues ppResults);
    void Content(out IPortableDeviceContent ppContent);
    void Capabilities(out IntPtr ppCapabilities);
    void Cancel();
    void Close();
}

[ComImport, Guid("6A96ED84-7C73-4480-9938-BF5AF477D426"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPortableDeviceContent
{
    void EnumObjects(uint dwFlags, [MarshalAs(UnmanagedType.LPWStr)] string pszParentObjectID, IntPtr pFilter, out IntPtr ppEnum);
    void Properties(out IPortableDeviceProperties ppProperties);
}

[ComImport, Guid("7F6D695C-03DF-4439-A809-59266BEEE3A6"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPortableDeviceProperties
{
    void GetSupportedProperties([MarshalAs(UnmanagedType.LPWStr)] string pszObjectID, out IPortableDeviceKeyCollection ppKeys);
    void GetPropertyAttributes([MarshalAs(UnmanagedType.LPWStr)] string pszObjectID, ref PROPERTYKEY key, out IntPtr ppAttributes);
    void GetValues([MarshalAs(UnmanagedType.LPWStr)] string pszObjectID, IPortableDeviceKeyCollection? pKeys, out IPortableDeviceValues ppValues);
}

[ComImport, Guid("DADA2357-E0AD-492E-98DB-DD61C53BA353"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPortableDeviceKeyCollection
{
    void GetCount(out uint pcElems);
    void GetAt(uint dwIndex, out PROPERTYKEY pKey);
    void Add(ref PROPERTYKEY key);
    void Clear();
    void RemoveAt(uint dwIndex);
}

[ComImport, Guid("6848F6F2-3155-4F86-B6F5-263EEEAB3143"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPortableDeviceValues
{
    void GetCount(ref uint pcelt);                                            // 1
    void GetAt(uint index, out PROPERTYKEY pKey, out PROPVARIANT pValue);     // 2
    void SetValue(ref PROPERTYKEY key, IntPtr pValue);                        // 3
    void GetValue(ref PROPERTYKEY key, out PROPVARIANT pValue);               // 4
    void SetStringValue(ref PROPERTYKEY key, [MarshalAs(UnmanagedType.LPWStr)] string Value); // 5
    void GetStringValue(ref PROPERTYKEY key, [MarshalAs(UnmanagedType.LPWStr)] out string pValue); // 6
    void SetUnsignedIntegerValue(ref PROPERTYKEY key, uint Value);            // 7
    void GetUnsignedIntegerValue(ref PROPERTYKEY key, out uint pValue);       // 8
    // Slots 9-24: declared (correct vtable order) but unused by this app.
    void SetSignedIntegerValue();        // 9
    void GetSignedIntegerValue();        // 10
    void SetUnsignedLargeIntegerValue(); // 11
    void GetUnsignedLargeIntegerValue(); // 12
    void SetSignedLargeIntegerValue();   // 13
    void GetSignedLargeIntegerValue();   // 14
    void SetFloatValue();                // 15
    void GetFloatValue();                // 16
    void SetErrorValue();                // 17
    void GetErrorValue();                // 18
    void SetKeyValue();                  // 19
    void GetKeyValue();                  // 20
    void SetBoolValue();                 // 21
    void GetBoolValue();                 // 22
    void SetIUnknownValue();             // 23
    void GetIUnknownValue();             // 24
    void SetGuidValue(ref PROPERTYKEY key, ref Guid Value);                   // 25
    void GetGuidValue();                 // 26
    void SetBufferValue(ref PROPERTYKEY key, IntPtr pValue, uint cbValue);    // 27
    void GetBufferValue(ref PROPERTYKEY key, out IntPtr pValue, out uint pcbValue); // 28
    void SetIPortableDeviceValuesCollectionValue();      // 29
    void GetIPortableDeviceValuesCollectionValue();      // 30
    void SetIPortableDevicePropVariantCollectionValue(
        ref PROPERTYKEY key, IPortableDevicePropVariantCollection pValue);    // 31
    void GetIPortableDevicePropVariantCollectionValue(
        ref PROPERTYKEY key, out IPortableDevicePropVariantCollection pValue); // 32
}

[ComImport, Guid("89B2E422-4F1B-4316-BCEF-A44AFEA83EB3"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPortableDevicePropVariantCollection
{
    void GetCount(out uint pcElems);
    void GetAt(uint dwIndex, out PROPVARIANT pValue);
    void Add(ref PROPVARIANT pValue);
    void GetType(out ushort pvt);
    void ChangeType();
    void Clear();
}

/// <summary>
/// Blittable subset of the Win32 PROPVARIANT. The union starts at offset 8 on both
/// 32- and 64-bit (vt at 0, three WORDs reserved). Only scalar/pointer members we
/// read are declared; everything else is freed via <see cref="NativeMethods.PropVariantClear"/>.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 24)]
internal struct PROPVARIANT
{
    [FieldOffset(0)] public ushort vt;
    [FieldOffset(8)] public IntPtr pointerValue; // VT_LPWSTR / VT_LPSTR / VT_BSTR / VT_CLSID
    [FieldOffset(8)] public byte bVal;           // VT_UI1
    [FieldOffset(8)] public short iVal;          // VT_I2
    [FieldOffset(8)] public ushort uiVal;        // VT_UI2
    [FieldOffset(8)] public int intVal;          // VT_I4
    [FieldOffset(8)] public uint uintVal;        // VT_UI4
    [FieldOffset(8)] public long longVal;        // VT_I8
    [FieldOffset(8)] public ulong ulongVal;      // VT_UI8
    [FieldOffset(8)] public short boolVal;       // VT_BOOL
    [FieldOffset(8)] public uint caCount;        // counted-array element count
    [FieldOffset(16)] public IntPtr caElements;  // counted-array data pointer (forces 24-byte size)
}

internal static class NativeMethods
{
    [DllImport("ole32.dll")]
    public static extern int PropVariantClear(ref PROPVARIANT pvar);
}
