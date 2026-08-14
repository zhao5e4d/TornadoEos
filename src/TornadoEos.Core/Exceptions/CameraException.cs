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
        : base("当前未连接相机，请先连接相机再执行此操作。") { }
}

/// <summary>Raised when an operation requires service mode that has not been entered.</summary>
public sealed class ServiceModeRequiredException : CameraException
{
    public ServiceModeRequiredException()
        : base("此操作需要服务模式，请先进入服务模式。") { }
}

/// <summary>Raised when a requested language is not part of the camera's language table.</summary>
public sealed class UnsupportedLanguageException : CameraException
{
    public UnsupportedLanguageException(string isoCode)
        : base($"当前相机不支持语言“{isoCode}”。") { }
}
