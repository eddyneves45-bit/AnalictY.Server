using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Scada.Core.Models.SQLite;
using Scada.Data.Models;

namespace Scada.Api.Services;

internal sealed class AlertRuleEvaluator : IAlertRuleEvaluator
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IAlertRealtimeService _alertRealtimeService;
    private readonly ITelegramNotificationService _telegramNotificationService;
    private readonly ConcurrentDictionary<int, bool> _activeStates = new();

    public AlertRuleEvaluator(
        IServiceScopeFactory scopeFactory,
        IAlertRealtimeService alertRealtimeService,
        ITelegramNotificationService telegramNotificationService)
    {
        _scopeFactory = scopeFactory;
        _alertRealtimeService = alertRealtimeService;
        _telegramNotificationService = telegramNotificationService;
    }

    public async Task EvaluateAsync(TagValueEnvelope envelope, CancellationToken cancellationToken = default)
    {
        if (!TryReadNumericValue(envelope.Value, out var numericValue))
        {
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ScadaDbContext>();
        var rules = await dbContext.AlertRules
            .Where(rule => rule.IsActive && rule.TagConfigId == envelope.TagId)
            .ToListAsync(cancellationToken);

        if (rules.Count == 0)
        {
            return;
        }

        var machineIds = await dbContext.MachineTagMaps
            .Where(map => map.IsActive && map.TagConfigId == envelope.TagId)
            .Select(map => map.MachineId)
            .ToListAsync(cancellationToken);

        foreach (var rule in rules)
        {
            var isTriggered = Compare(numericValue, rule.Operator, rule.LimitValue);
            var wasTriggered = _activeStates.GetOrAdd(rule.Id, false);

            if (isTriggered && !wasTriggered)
            {
                var createdAlerts = new List<Alert>();
                foreach (var machineId in machineIds.DefaultIfEmpty(null))
                {
                    var alert = new Alert
                    {
                        AlertType = "tag_limit",
                        Severity = rule.Severity,
                        Title = rule.Name,
                        Message = rule.Message,
                        MachineId = machineId,
                        Metadata = JsonSerializer.Serialize(new
                        {
                            rule_id = rule.Id,
                            tag_id = envelope.TagId,
                            tag_name = envelope.TagName,
                            current_value = numericValue,
                            @operator = rule.Operator,
                            limit_value = rule.LimitValue
                        }),
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };
                    dbContext.Alerts.Add(alert);
                    createdAlerts.Add(alert);
                }

                await dbContext.SaveChangesAsync(cancellationToken);
                foreach (var alert in createdAlerts)
                {
                    await _alertRealtimeService.PublishCreatedAsync(alert, cancellationToken);
                    await _telegramNotificationService.SendAlertAsync(
                        alert,
                        rule.TelegramConnectionId,
                        ParseRecipientIds(rule.TelegramRecipientIds),
                        cancellationToken);
                }
            }

            _activeStates[rule.Id] = isTriggered;
        }
    }

    private static bool TryReadNumericValue(object? value, out double numericValue)
    {
        switch (value)
        {
            case null:
                numericValue = default;
                return false;
            case JsonElement element when element.ValueKind == JsonValueKind.Number:
                return element.TryGetDouble(out numericValue);
            case JsonElement element when element.ValueKind == JsonValueKind.String:
                return double.TryParse(element.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out numericValue);
            case IConvertible convertible:
                try
                {
                    numericValue = convertible.ToDouble(CultureInfo.InvariantCulture);
                    return true;
                }
                catch
                {
                    numericValue = default;
                    return false;
                }
            default:
                numericValue = default;
                return false;
        }
    }

    private static bool Compare(double currentValue, string @operator, double limitValue) =>
        @operator switch
        {
            ">" => currentValue > limitValue,
            ">=" => currentValue >= limitValue,
            "<" => currentValue < limitValue,
            "<=" => currentValue <= limitValue,
            "=" => currentValue.Equals(limitValue),
            "!=" => !currentValue.Equals(limitValue),
            _ => false
        };

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
