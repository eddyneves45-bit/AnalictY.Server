namespace Scada.Api.Services;

internal sealed record IndustrialMetricsSnapshot(
    long ProcessedMessages,
    long FailedMessages,
    double LastProcessingDelaySeconds,
    double MaxProcessingDelaySeconds);
