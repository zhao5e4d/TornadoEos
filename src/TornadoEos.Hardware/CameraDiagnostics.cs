using System;
using System.Collections.Generic;
using System.Runtime.Versioning;
using System.Text;

namespace TornadoEos.Hardware;

/// <summary>Combined outcome of all read-only camera diagnostics.</summary>
public sealed record CameraDiagnosticsResult(
    IReadOnlyList<WpdProbeDevice> WpdDevices,
    IReadOnlyList<MtpCapabilities> MtpCapabilities,
    IReadOnlyList<EosSessionResult> EosSessions,
    string ReportText,
    string SummaryText);

/// <summary>
/// Runs the full read-only diagnostic suite (WPD properties, MTP vendor opcodes,
/// EOS session enumeration) and builds a human-readable report.
/// </summary>
[SupportedOSPlatform("windows")]
public static class CameraDiagnostics
{
    public static CameraDiagnosticsResult Run(Action<string>? log = null)
    {
        var wpd = WpdPropertyProbe.Run(log);
        var mtp = new List<MtpCapabilities>();
        var eos = new List<EosSessionResult>();

        foreach (var dev in wpd)
        {
            if (!dev.IsCanon)
                continue;

            log?.Invoke($"MTP capability probe on {dev.Model ?? "Canon device"}...");
            mtp.Add(MtpCommandProbe.ProbeDevice(dev.DeviceId, dev.Model, log));

            log?.Invoke($"EOS session probe on {dev.Model ?? "Canon device"} (read-only remote mode)...");
            eos.Add(EosSessionProbe.ProbeDevice(dev.DeviceId, dev.Model, log));
        }

        string report = BuildReport(wpd, mtp, eos);
        string summary = BuildSummary(wpd, mtp, eos);
        return new CameraDiagnosticsResult(wpd, mtp, eos, report, summary);
    }

    public static string BuildReport(
        IReadOnlyList<WpdProbeDevice> wpd,
        IReadOnlyList<MtpCapabilities> mtp,
        IReadOnlyList<EosSessionResult> eos)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Tornado EOS · Camera diagnostics (read-only)");
        sb.AppendLine(new string('=', 78));
        sb.AppendLine();

        if (wpd.Count == 0)
        {
            sb.AppendLine("No portable devices found.");
            sb.AppendLine("Connect the camera via USB, power it on, and run again.");
            return sb.ToString();
        }

        AppendWpdSection(sb, wpd);
        AppendMtpSection(sb, mtp);
        AppendEosSection(sb, eos);

        sb.AppendLine();
        sb.AppendLine(new string('=', 78));
        sb.AppendLine("All probes are read-only / reversible. No property was written and");
        sb.AppendLine("service mode was not entered.");
        return sb.ToString();
    }

    public static string BuildSummary(
        IReadOnlyList<WpdProbeDevice> wpd,
        IReadOnlyList<MtpCapabilities> mtp,
        IReadOnlyList<EosSessionResult> eos)
    {
        if (wpd.Count == 0)
            return "No camera detected";

        var canon = new List<WpdProbeDevice>();
        foreach (var d in wpd)
            if (d.IsCanon) canon.Add(d);

        if (canon.Count == 0)
            return $"{wpd.Count} device(s), no Canon camera";

        var parts = new List<string>();
        foreach (var d in canon)
            parts.Add(d.Model ?? d.FriendlyName ?? "Canon");

        int wpdProps = 0, opcodes = 0, eosProps = 0;
        foreach (var d in canon) wpdProps += d.Properties.Count;
        foreach (var c in mtp) opcodes += c.VendorOperationCodes.Count;
        foreach (var s in eos) eosProps += s.Properties.Count;

        return $"{string.Join(", ", parts)} · {wpdProps} WPD props · {opcodes} vendor opcodes · {eosProps} EOS props";
    }

    private static void AppendWpdSection(StringBuilder sb, IReadOnlyList<WpdProbeDevice> devices)
    {
        sb.AppendLine("WPD / PTP DEVICE PROPERTIES");
        sb.AppendLine(new string('-', 78));

        foreach (var dev in devices)
        {
            sb.AppendLine();
            sb.AppendLine($"Device: {dev.Model ?? dev.FriendlyName ?? "(unknown)"}{(dev.IsCanon ? " [CANON]" : "")}");
            sb.AppendLine($"  Manufacturer : {dev.Manufacturer ?? "-"}");
            sb.AppendLine($"  Firmware     : {FindFirmware(dev) ?? "-"}");
            sb.AppendLine($"  Serial       : {FindSerial(dev) ?? "-"}");
            sb.AppendLine($"  DeviceId     : {dev.DeviceId}");
            sb.AppendLine();

            foreach (var p in dev.Properties)
            {
                string label = p.KnownName ?? $"VENDOR {p.Pid} (0x{p.Pid:X4})";
                sb.AppendLine($"  {Trunc(label, 36),-36} {p.TypeName,-7} {Trunc(p.Value, 48)}");
            }

            int vendorCount = 0;
            foreach (var p in dev.Properties)
                if (p.IsVendorOrUnknown) vendorCount++;

            sb.AppendLine();
            sb.AppendLine($"  {dev.Properties.Count} properties, {vendorCount} vendor/unknown.");
        }
        sb.AppendLine();
    }

    private static void AppendMtpSection(StringBuilder sb, IReadOnlyList<MtpCapabilities> caps)
    {
        sb.AppendLine("MTP VENDOR CAPABILITIES");
        sb.AppendLine(new string('-', 78));

        if (caps.Count == 0)
        {
            sb.AppendLine("  (no Canon device probed)");
            sb.AppendLine();
            return;
        }

        foreach (var cap in caps)
        {
            sb.AppendLine();
            sb.AppendLine($"Device: {cap.Model ?? "(canon)"}");
            if (cap.Error is not null)
                sb.AppendLine($"  Error: {cap.Error}");
            sb.AppendLine($"  Vendor extension : {cap.VendorExtensionDescription ?? "(none / not reported)"}");
            sb.AppendLine($"  Vendor opcodes   : {cap.VendorOperationCodes.Count}");
            if (cap.VendorOperationCodes.Count > 0)
            {
                var codes = new List<string>();
                foreach (var c in cap.VendorOperationCodes)
                    codes.Add($"0x{c:X4}");
                foreach (var line in Chunk(codes, 8))
                    sb.AppendLine($"    {string.Join("  ", line)}");
            }
        }
        sb.AppendLine();
    }

    private static void AppendEosSection(StringBuilder sb, IReadOnlyList<EosSessionResult> sessions)
    {
        sb.AppendLine("EOS SESSION (read-only remote mode)");
        sb.AppendLine(new string('-', 78));

        if (sessions.Count == 0)
        {
            sb.AppendLine("  (no Canon device probed)");
            sb.AppendLine();
            return;
        }

        foreach (var s in sessions)
        {
            sb.AppendLine();
            sb.AppendLine($"Device: {s.Model ?? "(canon)"}");
            if (s.Error is not null)
                sb.AppendLine($"  Error: {s.Error}");
            sb.AppendLine($"  EOS events     : {s.EventsSupported.Count}");
            sb.AppendLine($"  EOS properties : {s.Properties.Count}");
            if (s.Properties.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"  {"Code",-8} {"Name",-22} Current value");
                sb.AppendLine("  " + new string('-', 74));
                foreach (var p in s.Properties)
                {
                    string name = p.KnownName ?? "(unknown)";
                    sb.AppendLine($"  0x{p.Code:X4}   {Trunc(name, 22),-22} {Trunc(p.ValueText, 40)}");
                }
            }
        }
        sb.AppendLine();
    }

    private static string? FindFirmware(WpdProbeDevice dev)
    {
        foreach (var p in dev.Properties)
            if (p.KnownName == "WPD_DEVICE_FIRMWARE_VERSION")
                return p.Value;
        return null;
    }

    private static string? FindSerial(WpdProbeDevice dev)
    {
        foreach (var p in dev.Properties)
            if (p.KnownName == "WPD_DEVICE_SERIAL_NUMBER")
                return p.Value;
        return null;
    }

    private static string Trunc(string s, int max) =>
        s.Length <= max ? s : s[..(max - 1)] + "…";

    private static IEnumerable<IReadOnlyList<string>> Chunk(List<string> items, int size)
    {
        for (int i = 0; i < items.Count; i += size)
            yield return items.GetRange(i, Math.Min(size, items.Count - i));
    }
}
