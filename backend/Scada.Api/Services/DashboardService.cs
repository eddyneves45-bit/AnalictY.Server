using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using Scada.Data.Models;

namespace Scada.Api.Services;

internal class DashboardService : IDashboardService
{
    private readonly ScadaDbContext _dbContext;
    private readonly ISystemTimeService _timeService;

    public DashboardService(ScadaDbContext dbContext, ISystemTimeService timeService)
    {
        _dbContext = dbContext;
        _timeService = timeService;
    }

    public async Task<object> GetOverviewAsync(CancellationToken cancellationToken = default)
    {
        var totalMachines = await _dbContext.Machines.CountAsync(m => m.IsActive, cancellationToken);
        var machineStates = await _dbContext.MachineStates.ToListAsync(cancellationToken);
        var runningMachines = machineStates.Count(s => s.State == "RUNNING");
        var stoppedMachines = totalMachines - runningMachines;
        var alertsCount = await _dbContext.Alerts.CountAsync(a => !a.IsAcknowledged, cancellationToken);
        var productionToday = await GetProductionTodayAsync(null, cancellationToken);

        return new
        {
            total_machines = totalMachines,
            running_machines = runningMachines,
            stopped_machines = stoppedMachines,
            alerts_count = alertsCount,
            production_today = productionToday
        };
    }

    private async Task<double> GetProductionTodayAsync(string? machineId, CancellationToken cancellationToken)
    {
        var config = await _dbContext.MySqlConfigs
            .AsNoTracking()
            .Where(item => item.IsActive && item.Provider != "SQLServer")
            .OrderByDescending(item => item.IsPrimary)
            .ThenByDescending(item => item.IsLocal)
            .ThenBy(item => item.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (config == null)
        {
            return 0;
        }

        var timeZone = await _timeService.GetConfiguredTimeZoneAsync(cancellationToken);
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, timeZone);
        var localStart = localNow.Date;
        var utcStart = _timeService.LocalToUtc(localStart, timeZone);
        var utcEnd = _timeService.LocalToUtc(localStart.AddDays(1), timeZone);

        await using var connection = new MySqlConnection(BuildConnectionString(config));
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COALESCE(SUM(quantidade), 0)
            FROM eventos_producao
            WHERE (@machine_id IS NULL OR id_maquina = @machine_id)
              AND ocorrido_em >= @from
              AND ocorrido_em < @to
            """;
        command.Parameters.AddWithValue("@machine_id", string.IsNullOrWhiteSpace(machineId) ? DBNull.Value : machineId);
        command.Parameters.AddWithValue("@from", utcStart);
        command.Parameters.AddWithValue("@to", utcEnd);
        return Convert.ToDouble(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static string BuildConnectionString(Scada.Core.Models.SQLite.MySqlConfig config) =>
        new MySqlConnectionStringBuilder
        {
            Server = config.Host,
            Port = (uint)config.Port,
            UserID = config.User,
            Password = config.Password,
            Database = config.Database,
            Pooling = true,
            MaximumPoolSize = (uint)Math.Max(config.PoolSize, 1),
            SslMode = MySqlSslMode.None,
            AllowPublicKeyRetrieval = true,
            ConnectionTimeout = 5
        }.ConnectionString;

    public async Task<object> GetMachineStatusAsync(string machineId, CancellationToken cancellationToken = default)
    {
        var machineState = await _dbContext.MachineStates.FirstOrDefaultAsync(s => s.MachineId == machineId, cancellationToken);
        if (machineState == null)
        {
            return new
            {
                machine_id = machineId,
                status = "unknown",
                data = new { },
                timestamp = ""
            };
        }

        return new
        {
            machine_id = machineId,
            status = machineState.State,
            data = new
            {
                production_count = await GetProductionTodayAsync(machineId, cancellationToken),
                current_speed = machineState.CurrentSpeed,
                average_speed = machineState.AverageSpeed,
                time_running = machineState.TimeRunning,
                time_stopped = machineState.TimeStopped,
                time_fault = machineState.TimeFault,
                time_setup = machineState.TimeSetup
            },
            timestamp = machineState.LastUpdate.ToString("o")
        };
    }
}
