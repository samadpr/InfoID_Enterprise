using System;

namespace InfoID.Infrastructure.Persistence;

/// <summary>
/// Minimal, dependency-free startup/diagnostic logger. Deliberately does not pull in
/// Microsoft.Extensions.Logging so Phase 1 doesn't add a new package dependency you'd
/// have to restore before this compiles -- swap for real structured logging later if
/// you want it, the call sites (DatabaseInitializer) won't need to change.
/// </summary>
public interface IStartupLogger
{
    void Info(string message);
    void Warn(string message);
    void Error(string message, Exception? exception = null);
}