namespace Scada.Api.Services;

internal sealed record RecentLogEntry(
    DateTime Timestamp,
    string Level,
    string Category,
    string Message,
    string? Exception);
