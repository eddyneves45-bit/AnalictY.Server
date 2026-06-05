using System.Globalization;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using Scada.Core.Models.SQLite;
using Scada.Data.Models;

namespace Scada.Api.Services;

internal sealed class ShiftService : IShiftService
{
    private readonly ScadaDbContext _dbContext;

    public ShiftService(ScadaDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<object> ListAsync(CancellationToken cancellationToken = default)
    {
        var config = await GetPrimaryMySqlConfigAsync(cancellationToken);
        if (config == null) return Array.Empty<object>();

        await using var connection = new MySqlConnection(BuildConnectionString(config));
        await connection.OpenAsync(cancellationToken);
        await EnsureShiftAccountingColumnAsync(connection, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, codigo, nome, hora_inicio, hora_fim, ativo, contabilizar_producao, criado_em, atualizado_em
            FROM turnos
            ORDER BY hora_inicio, id
            """;

        var items = new List<object>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new
            {
                id = reader.GetInt64(0),
                codigo = reader.GetString(1),
                nome = reader.GetString(2),
                hora_inicio = TimeOnly.FromTimeSpan(reader.GetTimeSpan(3)),
                hora_fim = TimeOnly.FromTimeSpan(reader.GetTimeSpan(4)),
                ativo = reader.GetBoolean(5),
                contabilizar_producao = reader.GetBoolean(6),
                criado_em = reader.GetDateTime(7),
                atualizado_em = reader.GetDateTime(8)
            });
        }

        return items;
    }

    public async Task<ApplicationServiceResult> UpsertAsync(ShiftRequest request, CancellationToken cancellationToken = default)
    {
        var codigo = request.codigo.Trim();
        var nome = request.nome.Trim();
        if (string.IsNullOrWhiteSpace(codigo) || string.IsNullOrWhiteSpace(nome))
        {
            return ApplicationServiceResult.BadRequest("Informe código e nome do turno.");
        }

        if (!TryParseTime(request.hora_inicio, out var horaInicio) ||
            !TryParseTime(request.hora_fim, out var horaFim))
        {
            return ApplicationServiceResult.BadRequest("Informe horários válidos no formato HH:mm.");
        }

        var config = await GetPrimaryMySqlConfigAsync(cancellationToken);
        if (config == null)
        {
            return ApplicationServiceResult.NotFound("Nenhuma conexão MySQL ativa configurada.");
        }

        await using var connection = new MySqlConnection(BuildConnectionString(config));
        await connection.OpenAsync(cancellationToken);
        await EnsureShiftAccountingColumnAsync(connection, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = request.id.HasValue
            ? """
                UPDATE turnos
                SET codigo = @codigo,
                    nome = @nome,
                    hora_inicio = @hora_inicio,
                    hora_fim = @hora_fim,
                    ativo = @ativo,
                    contabilizar_producao = @contabilizar_producao,
                    atualizado_em = UTC_TIMESTAMP(6)
                WHERE id = @id
                """
            : """
                INSERT INTO turnos
                    (codigo, nome, hora_inicio, hora_fim, ativo, contabilizar_producao, criado_em, atualizado_em)
                VALUES
                    (@codigo, @nome, @hora_inicio, @hora_fim, @ativo, @contabilizar_producao, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6))
                """;
        command.Parameters.AddWithValue("@id", request.id);
        command.Parameters.AddWithValue("@codigo", codigo);
        command.Parameters.AddWithValue("@nome", nome);
        command.Parameters.AddWithValue("@hora_inicio", horaInicio.ToTimeSpan());
        command.Parameters.AddWithValue("@hora_fim", horaFim.ToTimeSpan());
        command.Parameters.AddWithValue("@ativo", request.ativo);
        command.Parameters.AddWithValue("@contabilizar_producao", request.contabilizar_producao);

        try
        {
            var affected = await command.ExecuteNonQueryAsync(cancellationToken);
            if (request.id.HasValue && affected == 0)
            {
                return ApplicationServiceResult.NotFound("Turno não encontrado.");
            }
        }
        catch (MySqlException exception) when (exception.Number == 1062)
        {
            return ApplicationServiceResult.BadRequest("Já existe um turno com esse código.");
        }

        return ApplicationServiceResult.Ok(new { success = true });
    }

    public async Task<ApplicationServiceResult> DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        var config = await GetPrimaryMySqlConfigAsync(cancellationToken);
        if (config == null)
        {
            return ApplicationServiceResult.NotFound("Nenhuma conexão MySQL ativa configurada.");
        }

        await using var connection = new MySqlConnection(BuildConnectionString(config));
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM turnos WHERE id = @id";
        command.Parameters.AddWithValue("@id", id);

        try
        {
            var affected = await command.ExecuteNonQueryAsync(cancellationToken);
            return affected == 0
                ? ApplicationServiceResult.NotFound("Turno não encontrado.")
                : ApplicationServiceResult.Ok(new { message = "Turno excluído" });
        }
        catch (MySqlException exception) when (exception.Number == 1451)
        {
            return ApplicationServiceResult.BadRequest("Este turno já possui históricos vinculados e não pode ser excluído.");
        }
    }

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

    private static async Task EnsureShiftAccountingColumnAsync(MySqlConnection connection, CancellationToken cancellationToken)
    {
        await using var check = connection.CreateCommand();
        check.CommandText = """
            SELECT COUNT(*)
            FROM information_schema.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = 'turnos'
              AND COLUMN_NAME = 'contabilizar_producao'
            """;
        var exists = Convert.ToInt32(await check.ExecuteScalarAsync(cancellationToken)) > 0;
        if (exists)
        {
            return;
        }

        await using var alter = connection.CreateCommand();
        alter.CommandText = "ALTER TABLE turnos ADD COLUMN contabilizar_producao BOOLEAN NOT NULL DEFAULT TRUE AFTER ativo";
        await alter.ExecuteNonQueryAsync(cancellationToken);
    }

    private static bool TryParseTime(string? value, out TimeOnly time) =>
        TimeOnly.TryParseExact(
            value,
            ["HH:mm", "H:mm", "HH:mm:ss", "H:mm:ss"],
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out time);
}
