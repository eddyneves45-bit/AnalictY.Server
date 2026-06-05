namespace Scada.Api.Services;

internal interface ISystemTimeService
{
    Task<TimeZoneInfo> GetConfiguredTimeZoneAsync(CancellationToken cancellationToken = default);
    DateTime NormalizeToLocal(DateTime value, TimeZoneInfo timeZone);
    DateTime LocalToUtc(DateTime value, TimeZoneInfo timeZone);
    DateTime UtcToLocal(DateTime value, TimeZoneInfo timeZone);
    (DateTime LocalFrom, DateTime LocalTo, DateTime UtcFrom, DateTime UtcTo) BuildWindow(DateTime from, DateTime to, TimeZoneInfo timeZone);
}
