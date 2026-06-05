using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using Scada.Core.Models.SQLite;
using Scada.Data.Models;

namespace Scada.Api.Services;

internal sealed class MachineGoalService : IMachineGoalService
{
    private readonly ScadaDbContext _dbContext;

    public MachineGoalService(ScadaDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<object> ListAsync(int machineId, CancellationToken cancellationToken = default)
    {
        if (!await MachineExistsAsync(machineId, cancellationToken))
        {
            return Array.Empty<object>();
        }

        var config = await GetPrimaryMySqlConfigAsync(cancellationToken);
        if (config == null)
        {
            return Array.Empty<object>();
        }

        await using var connection = new MySqlConnection(BuildConnectionString(config));
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, id_maquina, meta_producao_dia, meta_producao_hora,
                   tempo_ciclo_ideal_segundos, vigente_de, vigente_ate, ativo,
                   criado_em, atualizado_em
            FROM metas_maquina
            WHERE id_maquina = @id_maquina
            ORDER BY vigente_de DESC, id DESC
            """;
        command.Parameters.AddWithValue("@id_maquina", machineId.ToString());

        var items = new List<object>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new
            {
                id = reader.GetInt64(0),
                machine_id = reader.GetString(1),
                meta_producao_dia = reader.IsDBNull(2) ? (double?)null : reader.GetDouble(2),
                meta_producao_hora = reader.IsDBNull(3) ? (double?)null : reader.GetDouble(3),
                tempo_ciclo_ideal_segundos = reader.IsDBNull(4) ? (double?)null : reader.GetDouble(4),
                vigente_de = reader.GetDateTime(5),
                vigente_ate = reader.IsDBNull(6) ? (DateTime?)null : reader.GetDateTime(6),
                ativo = reader.GetBoolean(7),
                criado_em = reader.GetDateTime(8),
                atualizado_em = reader.GetDateTime(9)
            });
        }

        return items;
    }

    public async Task<ApplicationServiceResult> CreateAsync(int machineId, MachineGoalRequest request, CancellationToken cancellationToken = default)
    {
        if (!await MachineExistsAsync(machineId, cancellationToken))
        {
            return ApplicationServiceResult.NotFound("Máquina não encontrada.");
        }

        if (request.meta_producao_dia is < 0 ||
            request.meta_producao_hora is < 0 ||
            request.tempo_ciclo_ideal_segundos is < 0)
        {
            return ApplicationServiceResult.BadRequest("Metas e tempo de ciclo não podem ser negativos.");
        }

        if (request.vigente_ate.HasValue && request.vigente_ate <= request.vigente_de)
        {
            return ApplicationServiceResult.BadRequest("A vigência final deve ser maior que a inicial.");
        }

        var config = await GetPrimaryMySqlConfigAsync(cancellationToken);
        if (config == null)
        {
            return ApplicationServiceResult.NotFound("Nenhuma conexão MySQL ativa configurada.");
        }

        await using var connection = new MySqlConnection(BuildConnectionString(config));
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        if (request.ativo)
        {
            await using var closeCurrent = connection.CreateCommand();
            closeCurrent.Transaction = transaction;
            closeCurrent.CommandText = """
                UPDATE metas_maquina
                SET ativo = FALSE,
                    vigente_ate = CASE
                        WHEN vigente_ate IS NULL OR vigente_ate > @vigente_de THEN @vigente_de
                        ELSE vigente_ate
                    END,
                    atualizado_em = UTC_TIMESTAMP(6)
                WHERE id_maquina = @id_maquina
                  AND ativo = TRUE
                """;
            closeCurrent.Parameters.AddWithValue("@id_maquina", machineId.ToString());
            closeCurrent.Parameters.AddWithValue("@vigente_de", request.vigente_de);
            await closeCurrent.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO metas_maquina
                (id_maquina, meta_producao_dia, meta_producao_hora, tempo_ciclo_ideal_segundos,
                 vigente_de, vigente_ate, ativo, criado_em, atualizado_em)
            VALUES
                (@id_maquina, @meta_producao_dia, @meta_producao_hora, @tempo_ciclo_ideal_segundos,
                 @vigente_de, @vigente_ate, @ativo, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6));
            SELECT LAST_INSERT_ID();
            """;
        insert.Parameters.AddWithValue("@id_maquina", machineId.ToString());
        insert.Parameters.AddWithValue("@meta_producao_dia", request.meta_producao_dia);
        insert.Parameters.AddWithValue("@meta_producao_hora", request.meta_producao_hora);
        insert.Parameters.AddWithValue("@tempo_ciclo_ideal_segundos", request.tempo_ciclo_ideal_segundos);
        insert.Parameters.AddWithValue("@vigente_de", request.vigente_de);
        insert.Parameters.AddWithValue("@vigente_ate", request.vigente_ate);
        insert.Parameters.AddWithValue("@ativo", request.ativo);
        var id = Convert.ToInt64(await insert.ExecuteScalarAsync(cancellationToken));

        await transaction.CommitAsync(cancellationToken);
        return ApplicationServiceResult.Ok(new { success = true, id });
    }

    private Task<bool> MachineExistsAsync(int machineId, CancellationToken cancellationToken) =>
        _dbContext.Machines.AnyAsync(machine => machine.Id == machineId, cancellationToken);

    private async Task<MySqlConfig?> GetPrimaryMySqlConfigAsync(CancellationToken cancellationToken) =>
        await _dbContext.MySqlConfigs.AsNoTracking()
            .Where(config => config.IsActive && config.Provider != "SQLServer")
            .OrderByDescending(config => config.IsPrimary)
            .ThenByDescending(config => config.IsLocal)
            .ThenBy(config => config.Id)
            .FirstOrDefaultAsync(cancellationToken);

    private static string BuildConnectionString(MySqlConfig config) =>
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
}
