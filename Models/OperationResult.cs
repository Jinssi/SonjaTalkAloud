namespace Sonja.ReadAloud.Models;

public sealed record OperationResult(bool Success, string Message, bool WasStopped = false)
{
    public static OperationResult Ok(string message = "Done.") => new(true, message);

    public static OperationResult Fail(string message) => new(false, message);

    public static OperationResult Stopped(string message = "Speech stopped.") => new(false, message, true);
}

public enum StatusLevel
{
    Ready,
    Working,
    Speaking,
    Warning,
    Error
}

public enum NotificationKind
{
    Info,
    Warning,
    Error
}