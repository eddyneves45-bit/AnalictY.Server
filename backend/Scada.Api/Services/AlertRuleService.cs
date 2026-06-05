using Microsoft.EntityFrameworkCore;
using Scada.Core.Models.SQLite;
using Scada.Data.Models;
using System.Text.Json;

namespace Scada.Api.Services;

internal sealed class AlertRuleService : IAlertRuleService
{
    private static readonly HashSet<string> ValidOperators = [">", ">=", "<", "<=", "=", "!="];
    private static readonly HashSet<string> ValidSeverities = ["low", "medium", "high", "critical"];
    private readonly ScadaDbContext _dbContext;

    public AlertRuleService(ScadaDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<object> ListAsync(CancellationToken cancellationToken = default)
    {
        var rules = await _dbContext.AlertRules
            .OrderBy(rule => rule.Name)
            .Select(rule => new
            {
                rule.Id,
                rule.Name,
                rule.TagConfigId,
                tagName = _dbContext.TagConfigs
                    .Where(tag => tag.Id == rule.TagConfigId)
                    .Select(tag => tag.TagName)
                    .FirstOrDefault(),
                rule.Operator,
                rule.LimitValue,
                rule.Severity,
                rule.Message,
                rule.TelegramConnectionId,
                telegramRecipientIds = ParseRecipientIds(rule.TelegramRecipientIds),
                rule.IsActive,
                rule.CreatedAt,
                rule.UpdatedAt
            })
            .ToListAsync(cancellationToken);

        return new { rules, count = rules.Count };
    }

    public async Task<ApplicationServiceResult> CreateAsync(AlertRuleRequest request, CancellationToken cancellationToken = default)
    {
        var validation = await ValidateAsync(request, cancellationToken);
        if (validation is not null)
        {
            return validation;
        }

        var rule = new AlertRule
        {
            Name = request.name.Trim(),
            TagConfigId = request.tag_config_id,
            Operator = request.@operator,
            LimitValue = request.limit_value,
            Severity = request.severity,
            Message = request.message.Trim(),
            TelegramConnectionId = request.telegram_connection_id,
            TelegramRecipientIds = SerializeRecipientIds(request.telegram_recipient_ids),
            IsActive = request.is_active
        };

        _dbContext.AlertRules.Add(rule);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return ApplicationServiceResult.Ok(rule);
    }

    public async Task<ApplicationServiceResult> UpdateAsync(int id, AlertRuleRequest request, CancellationToken cancellationToken = default)
    {
        var rule = await _dbContext.AlertRules.FindAsync([id], cancellationToken);
        if (rule is null)
        {
            return ApplicationServiceResult.NotFound();
        }

        var validation = await ValidateAsync(request, cancellationToken);
        if (validation is not null)
        {
            return validation;
        }

        rule.Name = request.name.Trim();
        rule.TagConfigId = request.tag_config_id;
        rule.Operator = request.@operator;
        rule.LimitValue = request.limit_value;
        rule.Severity = request.severity;
        rule.Message = request.message.Trim();
        rule.TelegramConnectionId = request.telegram_connection_id;
        rule.TelegramRecipientIds = SerializeRecipientIds(request.telegram_recipient_ids);
        rule.IsActive = request.is_active;
        rule.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);
        return ApplicationServiceResult.Ok(rule);
    }

    public async Task<ApplicationServiceResult> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var rule = await _dbContext.AlertRules.FindAsync([id], cancellationToken);
        if (rule is null)
        {
            return ApplicationServiceResult.NotFound();
        }

        _dbContext.AlertRules.Remove(rule);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return ApplicationServiceResult.Ok(new { message = "Regra excluída" });
    }

    private async Task<ApplicationServiceResult?> ValidateAsync(AlertRuleRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.name) || string.IsNullOrWhiteSpace(request.message))
        {
            return ApplicationServiceResult.BadRequest("Nome e mensagem são obrigatórios.");
        }

        if (!ValidOperators.Contains(request.@operator))
        {
            return ApplicationServiceResult.BadRequest("Operador inválido.");
        }

        if (!ValidSeverities.Contains(request.severity))
        {
            return ApplicationServiceResult.BadRequest("Severidade inválida.");
        }

        if (!await _dbContext.TagConfigs.AnyAsync(tag => tag.Id == request.tag_config_id, cancellationToken))
        {
            return ApplicationServiceResult.BadRequest("TAG não encontrada.");
        }

        if (request.telegram_connection_id.HasValue)
        {
            var connectionExists = await _dbContext.TelegramConnections.AnyAsync(
                connection => connection.Id == request.telegram_connection_id.Value && connection.IsActive,
                cancellationToken);
            if (!connectionExists)
            {
                return ApplicationServiceResult.BadRequest("Conexão Telegram não encontrada ou inativa.");
            }
        }

        var recipientIds = (request.telegram_recipient_ids ?? [])
            .Where(id => id > 0)
            .Distinct()
            .ToList();
        if (recipientIds.Count > 0)
        {
            var validRecipients = await _dbContext.TelegramRecipients
                .Where(recipient => recipientIds.Contains(recipient.Id)
                    && recipient.IsActive
                    && (!request.telegram_connection_id.HasValue || recipient.ConnectionId == request.telegram_connection_id.Value))
                .Select(recipient => recipient.Id)
                .ToListAsync(cancellationToken);

            if (validRecipients.Count != recipientIds.Count)
            {
                return ApplicationServiceResult.BadRequest("Um ou mais destinatários Telegram são inválidos para a conexão escolhida.");
            }
        }

        return null;
    }

    private static string SerializeRecipientIds(List<int>? recipientIds)
    {
        var normalized = (recipientIds ?? [])
            .Where(id => id > 0)
            .Distinct()
            .ToList();
        return normalized.Count == 0 ? string.Empty : JsonSerializer.Serialize(normalized);
    }

    private static List<int> ParseRecipientIds(string? recipientIds)
    {
        if (string.IsNullOrWhiteSpace(recipientIds)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<int>>(recipientIds) ?? [];
        }
        catch
        {
            return [];
        }
    }
}
