namespace TornadoEos.Core.Models;

/// <summary>
/// Progress report emitted during a long-running service operation
/// (for example writing the menu-language property to firmware).
/// </summary>
/// <param name="Percent">Completion from 0 to 100.</param>
/// <param name="Message">Human-readable description of the current step.</param>
public readonly record struct ServiceProgress(int Percent, string Message);
