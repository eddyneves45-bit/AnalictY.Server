namespace Scada.Api.Services;

internal interface IAlertRuleEvaluator
{
    Task EvaluateAsync(TagValueEnvelope envelope, CancellationToken cancellationToken = default);
}
