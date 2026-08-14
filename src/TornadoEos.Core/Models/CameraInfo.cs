namespace TornadoEos.Core.Models;

/// <summary>
/// Identifying details read from a connected camera body.
/// </summary>
public sealed record CameraInfo(
    string ModelName,
    string SerialNumber,
    string FirmwareVersion,
    int BatteryPercent,
    string PortDescription);
