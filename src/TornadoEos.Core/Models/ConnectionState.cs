namespace TornadoEos.Core.Models;

/// <summary>
/// High-level lifecycle state of the link between the app and a camera.
/// </summary>
public enum ConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    /// <summary>Connected and the camera has entered service/factory mode.</summary>
    ServiceMode,
    Busy,
    Error,
}
