using System;

namespace TornadoEos.Core.Exceptions;

/// <summary>Base type for all camera communication / service errors.</summary>
public class CameraException : Exception
{
    public CameraException(string message) : base(message) { }
    public CameraException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>Raised when an operation requires a connection that is not present.</summary>
public sealed class CameraNotConnectedException : CameraException
{
    public CameraNotConnectedException()
        : base("No camera is connected. Connect a camera before performing this operation.") { }
}

/// <summary>Raised when an operation requires service mode that has not been entered.</summary>
public sealed class ServiceModeRequiredException : CameraException
{
    public ServiceModeRequiredException()
        : base("This operation requires service mode. Enter service mode first.") { }
}

/// <summary>Raised when a requested language is not part of the camera's language table.</summary>
public sealed class UnsupportedLanguageException : CameraException
{
    public UnsupportedLanguageException(string isoCode)
        : base($"The language '{isoCode}' is not available on this camera body.") { }
}
