using Microsoft.EntityFrameworkCore;
using Scada.Data.Models;

namespace Scada.Api.Services;

internal sealed class SystemTimeService : ISystemTimeService
{
    private readonly ScadaDbContext _dbContext;

    public SystemTimeService(ScadaDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<TimeZoneInfo> GetConfiguredTimeZoneAsync(CancellationToken cancellationToken = default)
    {
        var timeZoneId = await _dbContext.SystemSettings
            .AsNoTracking()
            .Where(setting => setting.Key == "TimeZoneId")
            .Select(setting => setting.Value)
            .FirstOrDefaultAsync(cancellationToken) ?? "America/Sao_Paulo";

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch
        {
            return TimeZoneInfo.Utc;
        }
    }

    public DateTime NormalizeToLocal(DateTime value, TimeZoneInfo timeZone)
    {
        if (value.Kind == DateTimeKind.Utc)
        {
            return TimeZoneInfo.ConvertTimeFromUtc(value, timeZone);
        }

        return DateTime.SpecifyKind(value, DateTimeKind.Unspecified);
    }

    public DateTime LocalToUtc(DateTime value, TimeZoneInfo timeZone)
    {
        var local = DateTime.SpecifyKind(value, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(local, timeZone);
    }

    public DateTime UtcToLocal(DateTime value, TimeZoneInfo timeZone)
    {
        var utc = value.Kind == DateTimeKind.Utc
            ? value
            : DateTime.SpecifyKind(value, DateTimeKind.Utc);
        return TimeZoneInfo.ConvertTimeFromUtc(utc, timeZone);
    }

    public (DateTime LocalFrom, DateTime LocalTo, DateTime UtcFrom, DateTime UtcTo) BuildWindow(
        DateTime from,
        DateTime to,
        TimeZoneInfo timeZone)
    {
        var localFrom = NormalizeToLocal(from, timeZone);
        var localTo = NormalizeToLocal(to, timeZone);
        return (localFrom, localTo, LocalToUtc(localFrom, timeZone), LocalToUtc(localTo, timeZone));
    }
}
