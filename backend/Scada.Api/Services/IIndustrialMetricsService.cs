namespace Scada.Api.Services;

internal interface IIndustrialMetricsService
{
    void RecordProcessed(TagValueEnvelope envelope);
    void RecordFailure();
    IndustrialMetricsSnapshot GetSnapshot();
}
