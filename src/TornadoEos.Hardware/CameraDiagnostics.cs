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

            log?.Invoke($"正在探测 {dev.Model ?? "佳能设备"} 的 MTP 能力……");
            mtp.Add(MtpCommandProbe.ProbeDevice(dev.DeviceId, dev.Model, log));

            log?.Invoke($"正在探测 {dev.Model ?? "佳能设备"} 的 EOS 会话（只读遥控模式）……");
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
        sb.AppendLine("Tornado EOS · 相机诊断报告（只读）");
        sb.AppendLine(new string('=', 78));
        sb.AppendLine();

        if (wpd.Count == 0)
        {
            sb.AppendLine("未找到便携设备。");
            sb.AppendLine("请通过 USB 连接并开启相机，然后重新运行诊断。");
            return sb.ToString();
        }

        AppendWpdSection(sb, wpd);
        AppendMtpSection(sb, mtp);
        AppendEosSection(sb, eos);

        sb.AppendLine();
        sb.AppendLine(new string('=', 78));
        sb.AppendLine("所有探测均为只读操作；没有写入任何属性，也没有进入服务模式。");
        return sb.ToString();
    }

    public static string BuildSummary(
        IReadOnlyList<WpdProbeDevice> wpd,
        IReadOnlyList<MtpCapabilities> mtp,
        IReadOnlyList<EosSessionResult> eos)
    {
        if (wpd.Count == 0)
            return "未检测到相机";

        var canon = new List<WpdProbeDevice>();
        foreach (var d in wpd)
            if (d.IsCanon) canon.Add(d);

        if (canon.Count == 0)
            return $"检测到 {wpd.Count} 个设备，但没有佳能相机";

        var parts = new List<string>();
        foreach (var d in canon)
            parts.Add(d.Model ?? d.FriendlyName ?? "Canon");

        int wpdProps = 0, opcodes = 0, eosProps = 0;
        foreach (var d in canon) wpdProps += d.Properties.Count;
        foreach (var c in mtp) opcodes += c.VendorOperationCodes.Count;
        foreach (var s in eos) eosProps += s.Properties.Count;

        return $"{string.Join(", ", parts)} · {wpdProps} 个 WPD 属性 · {opcodes} 个厂商操作码 · {eosProps} 个 EOS 属性";
    }

    private static void AppendWpdSection(StringBuilder sb, IReadOnlyList<WpdProbeDevice> devices)
    {
        sb.AppendLine("WPD / PTP 设备属性");
        sb.AppendLine(new string('-', 78));

        foreach (var dev in devices)
        {
            sb.AppendLine();
            sb.AppendLine($"设备：{dev.Model ?? dev.FriendlyName ?? "（未知）"}{(dev.IsCanon ? " [CANON]" : "")}");
            sb.AppendLine($"  制造商       : {dev.Manufacturer ?? "-"}");
            sb.AppendLine($"  固件版本     : {FindFirmware(dev) ?? "-"}");
            sb.AppendLine($"  序列号       : {FindSerial(dev) ?? "-"}");
            sb.AppendLine($"  设备 ID      : {dev.DeviceId}");
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
            sb.AppendLine($"  共 {dev.Properties.Count} 个属性，其中 {vendorCount} 个为厂商或未知属性。");
        }
        sb.AppendLine();
    }

    private static void AppendMtpSection(StringBuilder sb, IReadOnlyList<MtpCapabilities> caps)
    {
        sb.AppendLine("MTP 厂商能力");
        sb.AppendLine(new string('-', 78));

        if (caps.Count == 0)
        {
            sb.AppendLine("  （没有可探测的佳能设备）");
            sb.AppendLine();
            return;
        }

        foreach (var cap in caps)
        {
            sb.AppendLine();
            sb.AppendLine($"设备：{cap.Model ?? "（佳能）"}");
            if (cap.Error is not null)
                sb.AppendLine($"  错误：{cap.Error}");
            sb.AppendLine($"  厂商扩展说明：{cap.VendorExtensionDescription ?? "（无 / 未报告）"}");
            sb.AppendLine($"  厂商操作码：{cap.VendorOperationCodes.Count}");
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
        sb.AppendLine("EOS 会话（只读遥控模式）");
        sb.AppendLine(new string('-', 78));

        if (sessions.Count == 0)
        {
            sb.AppendLine("  （没有可探测的佳能设备）");
            sb.AppendLine();
            return;
        }

        foreach (var s in sessions)
        {
            sb.AppendLine();
            sb.AppendLine($"设备：{s.Model ?? "（佳能）"}");
            if (s.Error is not null)
                sb.AppendLine($"  错误：{s.Error}");
            sb.AppendLine($"  EOS 事件：{s.EventsSupported.Count}");
            sb.AppendLine($"  EOS 属性：{s.Properties.Count}");
            if (s.Properties.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"  {"代码",-8} {"名称",-22} 当前值");
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
