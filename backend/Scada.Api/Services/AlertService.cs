using Microsoft.EntityFrameworkCore;
using Scada.Data.Models;

namespace Scada.Api.Services;

internal class AlertService : IAlertService
{
    private const string RetentionSettingKey = "AlertRetentionDays";
    private const int DefaultRetentionDays = 1;
    private const int MinRetentionDays = 1;
    private const int MaxRetentionDays = 7;
    private readonly ScadaDbContext _dbContext;
    private readonly IAlertRealtimeService _alertRealtimeService;
    private readonly ISystemTimeService _timeService;

    public AlertService(
        ScadaDbContext dbContext,
        IAlertRealtimeService alertRealtimeService,
        ISystemTimeService timeService)
    {
        _dbContext = dbContext;
        _alertRealtimeService = alertRealtimeService;
        _timeService = timeService;
    }

    public async Task<object> ListAlertsAsync(
        string? machineId,
        string? alertType,
        string? severity,
        bool? isAcknowledged,
        int limit,
        CancellationToken cancellationToken = default)
    {
        await CleanupExpiredAsync(cancellationToken);

        var query = _dbContext.Alerts.AsQueryable();

        if (!string.IsNullOrEmpty(machineId))
            query = query.Where(a => a.MachineId == machineId);
        if (!string.IsNullOrEmpty(alertType))
            query = query.Where(a => a.AlertType == alertType);
        if (!string.IsNullOrEmpty(severity))
            query = query.Where(a => a.Severity == severity);
        if (isAcknowledged.HasValue)
            query = query.Where(a => a.IsAcknowledged == isAcknowledged.Value);

        var alerts = await query.OrderByDescending(a => a.CreatedAt).Take(Math.Clamp(limit, 1, 100)).ToListAsync(cancellationToken);
        return new { alerts, count = alerts.Count };
    }

    public async Task<object> GetRetentionAsync(CancellationToken cancellationToken = default)
    {
        var retentionDays = await GetRetentionDaysAsync(cancellationToken);
        return new
        {
            retention_days = retentionDays,
            min_days = MinRetentionDays,
            max_days = MaxRetentionDays
        };
    }

    public async Task<ApplicationServiceResult> SetRetentionAsync(AlertRetentionRequest request, CancellationToken cancellationToken = default)
    {
        var retentionDays = Math.Clamp(request.retention_days, MinRetentionDays, MaxRetentionDays);
        var setting = await _dbContext.SystemSettings.FirstOrDefaultAsync(item => item.Key == RetentionSettingKey, cancellationToken);
        if (setting == null)
        {
            _dbContext.SystemSettings.Add(new Scada.Core.Models.SQLite.SystemSetting
            {
                Key = RetentionSettingKey,
                Value = retentionDays.ToString(),
                UpdatedAt = DateTime.UtcNow
            });
        }
        else
        {
            setting.Value = retentionDays.ToString();
            setting.UpdatedAt = DateTime.UtcNow;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        await CleanupExpiredAsync(cancellationToken);

        return ApplicationServiceResult.Ok(new
        {
            retention_days = retentionDays,
            message = "Retenção de alertas salva"
        });
    }

    public async Task<object> CreateAlertAsync(AlertCreateRequest request, CancellationToken cancellationToken = default)
    {
        var alert = new Scada.Core.Models.SQLite.Alert
        {
            AlertType = request.alert_type,
            Severity = request.severity,
            Title = request.title,
            Message = request.message,
            MachineId = request.machine_id,
            Metadata = request.metadata,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _dbContext.Alerts.Add(alert);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _alertRealtimeService.PublishCreatedAsync(alert, cancellationToken);

        return alert;
    }

    public async Task<ApplicationServiceResult> AcknowledgeAlertAsync(int id, string acknowledgedBy, CancellationToken cancellationToken = default)
    {
        var alert = await _dbContext.Alerts.FindAsync(new object[] { id }, cancellationToken);
        if (alert == null)
        {
            return ApplicationServiceResult.NotFound();
        }

        alert.IsAcknowledged = true;
        alert.AcknowledgedBy = acknowledgedBy;
        alert.AcknowledgedAt = DateTime.UtcNow;
        alert.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);
        await _alertRealtimeService.PublishUpdatedAsync(alert, cancellationToken);
        return ApplicationServiceResult.Ok(new { message = "Alerta reconhecido" });
    }

    public async Task<ApplicationServiceResult> DeleteAlertAsync(int id, CancellationToken cancellationToken = default)
    {
        var alert = await _dbContext.Alerts.FindAsync(new object[] { id }, cancellationToken);
        if (alert == null)
        {
            return ApplicationServiceResult.NotFound();
        }

        _dbContext.Alerts.Remove(alert);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _alertRealtimeService.PublishDeletedAsync(id, cancellationToken);

        return ApplicationServiceResult.Ok(new { message = "Alerta excluído" });
    }

    private async Task<int> GetRetentionDaysAsync(CancellationToken cancellationToken)
    {
        var value = await _dbContext.SystemSettings
            .AsNoTracking()
            .Where(item => item.Key == RetentionSettingKey)
            .Select(item => item.Value)
            .FirstOrDefaultAsync(cancellationToken);

        return int.TryParse(value, out var retentionDays)
            ? Math.Clamp(retentionDays, MinRetentionDays, MaxRetentionDays)
            : DefaultRetentionDays;
    }

    private async Task CleanupExpiredAsync(CancellationToken cancellationToken)
    {
        var retentionDays = await GetRetentionDaysAsync(cancellationToken);
        var timeZone = await _timeService.GetConfiguredTimeZoneAsync(cancellationToken);
        var localNow = _timeService.UtcToLocal(DateTime.UtcNow, timeZone);
        var cutoffLocal = localNow.Date.AddDays(-Math.Max(retentionDays - 1, 0));
        var cutoffUtc = _timeService.LocalToUtc(cutoffLocal, timeZone);

        await _dbContext.Alerts
            .Where(alert => alert.CreatedAt < cutoffUtc)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
