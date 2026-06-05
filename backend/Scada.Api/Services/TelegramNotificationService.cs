using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Scada.Core.Models.SQLite;
using Scada.Data.Models;

namespace Scada.Api.Services;

internal sealed class TelegramNotificationService : ITelegramNotificationService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ITelegramNotificationQueue _telegramQueue;
    private readonly ILogger<TelegramNotificationService> _logger;

    public TelegramNotificationService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        IServiceScopeFactory scopeFactory,
        ITelegramNotificationQueue telegramQueue,
        ILogger<TelegramNotificationService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _scopeFactory = scopeFactory;
        _telegramQueue = telegramQueue;
        _logger = logger;
    }

    public async Task<object> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var targets = await GetActiveTargetsAsync(cancellationToken);
        var envToken = _configuration["Telegram:BotToken"];
        var envChatId = _configuration["Telegram:ChatId"];
        return new
        {
            enabled = targets.Count > 0 || IsConfigured(envToken, envChatId),
            dynamicConnections = targets.Select(item => item.ConnectionId).Distinct().Count(),
            recipients = targets.Count,
            botTokenConfigured = targets.Count > 0 || !string.IsNullOrWhiteSpace(envToken),
            chatIdConfigured = targets.Count > 0 || !string.IsNullOrWhiteSpace(envChatId),
            chatId = targets.Count > 0 ? $"{targets.Count} destinatário(s)" : MaskChatId(envChatId),
            cooldownMinutes = targets.Count > 0 ? targets.Min(item => item.CooldownMinutes) : GetFallbackCooldown().TotalMinutes
        };
    }

    public async Task<object> ListConnectionsAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ScadaDbContext>();
        var recipientCounts = await dbContext.TelegramRecipients
            .AsNoTracking()
            .GroupBy(item => item.ConnectionId)
            .Select(group => new { ConnectionId = group.Key, Count = group.Count(), Active = group.Count(item => item.IsActive) })
            .ToListAsync(cancellationToken);

        var counts = recipientCounts.ToDictionary(item => item.ConnectionId);
        var connections = await dbContext.TelegramConnections
            .AsNoTracking()
            .OrderByDescending(item => item.IsActive)
            .ThenBy(item => item.Name)
            .ToListAsync(cancellationToken);

        return connections.Select(item => new
            {
                id = item.Id,
                name = item.Name,
                bot_token_configured = item.BotToken != "",
                bot_token_masked = MaskToken(item.BotToken),
                default_chat_id = MaskChatId(item.DefaultChatId),
                is_active = item.IsActive,
                cooldown_minutes = item.CooldownMinutes,
                recipients = counts.ContainsKey(item.Id) ? counts[item.Id].Count : 0,
                active_recipients = counts.ContainsKey(item.Id) ? counts[item.Id].Active : 0,
                updated_at = item.UpdatedAt
            })
            .ToList();
    }

    public async Task<ApplicationServiceResult> UpsertConnectionAsync(int? id, TelegramConnectionRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.name))
        {
            return ApplicationServiceResult.BadRequest(new { message = "Informe o nome da conexão." });
        }

        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ScadaDbContext>();
        TelegramConnection connection;
        if (id.HasValue)
        {
            connection = await dbContext.TelegramConnections.FindAsync(new object[] { id.Value }, cancellationToken)
                ?? null!;
            if (connection == null)
            {
                return ApplicationServiceResult.NotFound(new { message = "Conexão Telegram não encontrada." });
            }
            if (!string.IsNullOrWhiteSpace(request.bot_token))
            {
                connection.BotToken = request.bot_token.Trim();
            }
        }
        else
        {
            if (string.IsNullOrWhiteSpace(request.bot_token))
            {
                return ApplicationServiceResult.BadRequest(new { message = "Informe o token do bot." });
            }

            connection = new TelegramConnection
            {
                BotToken = request.bot_token.Trim(),
                CreatedAt = DateTime.UtcNow
            };
            dbContext.TelegramConnections.Add(connection);
        }

        connection.Name = request.name.Trim();
        connection.DefaultChatId = string.IsNullOrWhiteSpace(request.default_chat_id) ? null : request.default_chat_id.Trim();
        connection.IsActive = request.is_active;
        connection.CooldownMinutes = Math.Clamp(request.cooldown_minutes, 1, 1440);
        connection.UpdatedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return ApplicationServiceResult.Ok(new { message = "Conexão Telegram salva.", id = connection.Id });
    }

    public async Task<ApplicationServiceResult> DeleteConnectionAsync(int id, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ScadaDbContext>();
        var connection = await dbContext.TelegramConnections.FindAsync(new object[] { id }, cancellationToken);
        if (connection == null) return ApplicationServiceResult.NotFound(new { message = "Conexão não encontrada." });

        var recipients = dbContext.TelegramRecipients.Where(item => item.ConnectionId == id);
        dbContext.TelegramRecipients.RemoveRange(recipients);
        dbContext.TelegramConnections.Remove(connection);
        await dbContext.SaveChangesAsync(cancellationToken);
        return ApplicationServiceResult.Ok(new { message = "Conexão Telegram excluída." });
    }

    public async Task<object> ListRecipientsAsync(int? connectionId, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ScadaDbContext>();
        var query = dbContext.TelegramRecipients.AsNoTracking();
        if (connectionId.HasValue) query = query.Where(item => item.ConnectionId == connectionId.Value);

        return await query
            .OrderByDescending(item => item.IsActive)
            .ThenBy(item => item.Name)
            .Select(item => new
            {
                id = item.Id,
                connection_id = item.ConnectionId,
                name = item.Name,
                chat_id = MaskChatId(item.ChatId),
                destination_type = item.DestinationType,
                is_active = item.IsActive,
                updated_at = item.UpdatedAt
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<ApplicationServiceResult> CaptureRecipientsAsync(int? connectionId, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ScadaDbContext>();
        var connection = connectionId.HasValue
            ? await dbContext.TelegramConnections.AsNoTracking().FirstOrDefaultAsync(item => item.Id == connectionId.Value, cancellationToken)
            : await dbContext.TelegramConnections.AsNoTracking()
                .Where(item => item.IsActive)
                .OrderBy(item => item.Id)
                .FirstOrDefaultAsync(cancellationToken);

        if (connection == null || string.IsNullOrWhiteSpace(connection.BotToken))
        {
            return ApplicationServiceResult.BadRequest(new { message = "Configure uma conexão Telegram ativa antes de capturar destinatários." });
        }

        var updates = await GetTelegramUpdatesAsync(connection.BotToken, cancellationToken);
        if (updates == null || !updates.Ok)
        {
            return ApplicationServiceResult.BadRequest(new { message = "Não foi possível consultar o Telegram. Verifique o token do bot." });
        }

        var existingChatIds = await dbContext.TelegramRecipients
            .AsNoTracking()
            .Where(item => item.ConnectionId == connection.Id)
            .Select(item => item.ChatId)
            .ToListAsync(cancellationToken);
        var existing = existingChatIds.ToHashSet(StringComparer.Ordinal);
        var candidates = updates.Result
            .Select(item => item.Message?.Chat ?? item.ChannelPost?.Chat ?? item.EditedMessage?.Chat)
            .Where(chat => chat?.Id != null)
            .GroupBy(chat => chat!.Id!.Value)
            .Select(group =>
            {
                var chat = group.Last()!;
                var chatId = chat.Id!.Value.ToString();
                var name = BuildChatName(chat);
                var destinationType = chat.Type == "private" ? "user" : chat.Type == "channel" ? "channel" : "group";
                return new
                {
                    connection_id = connection.Id,
                    name,
                    chat_id = chatId,
                    chat_id_masked = MaskChatId(chatId),
                    destination_type = destinationType,
                    already_registered = existing.Contains(chatId)
                };
            })
            .OrderBy(item => item.already_registered)
            .ThenBy(item => item.name)
            .ToList();

        return ApplicationServiceResult.Ok(new
        {
            connection_id = connection.Id,
            connection_name = connection.Name,
            bot_link = await GetBotLinkAsync(connection.BotToken, cancellationToken),
            candidates,
            message = candidates.Count == 0
                ? "Nenhuma conversa recente encontrada. Peça para a pessoa enviar /start ao bot e capture novamente."
                : $"{candidates.Count} conversa(s) encontrada(s)."
        });
    }

    public async Task<ApplicationServiceResult> UpsertRecipientAsync(int? id, TelegramRecipientRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.name) || (!id.HasValue && string.IsNullOrWhiteSpace(request.chat_id)))
        {
            return ApplicationServiceResult.BadRequest(new { message = "Informe nome e chat_id." });
        }

        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ScadaDbContext>();
        if (!await dbContext.TelegramConnections.AnyAsync(item => item.Id == request.connection_id, cancellationToken))
        {
            return ApplicationServiceResult.BadRequest(new { message = "Conexão Telegram inválida." });
        }

        if (!id.HasValue && !string.IsNullOrWhiteSpace(request.chat_id))
        {
            var chatId = request.chat_id.Trim();
            var alreadyExists = await dbContext.TelegramRecipients.AnyAsync(
                item => item.ConnectionId == request.connection_id && item.ChatId == chatId,
                cancellationToken);
            if (alreadyExists)
            {
                return ApplicationServiceResult.BadRequest(new { message = "Destinatário Telegram já cadastrado nesta conexão." });
            }
        }

        TelegramRecipient recipient;
        if (id.HasValue)
        {
            recipient = await dbContext.TelegramRecipients.FindAsync(new object[] { id.Value }, cancellationToken)
                ?? null!;
            if (recipient == null)
            {
                return ApplicationServiceResult.NotFound(new { message = "Destinatário Telegram não encontrado." });
            }
        }
        else
        {
            recipient = new TelegramRecipient { CreatedAt = DateTime.UtcNow };
            dbContext.TelegramRecipients.Add(recipient);
        }

        recipient.ConnectionId = request.connection_id;
        recipient.Name = request.name.Trim();
        if (!string.IsNullOrWhiteSpace(request.chat_id))
        {
            recipient.ChatId = request.chat_id.Trim();
        }
        recipient.DestinationType = string.IsNullOrWhiteSpace(request.destination_type) ? "user" : request.destination_type.Trim();
        recipient.IsActive = request.is_active;
        recipient.UpdatedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return ApplicationServiceResult.Ok(new { message = "Destinatário salvo.", id = recipient.Id });
    }

    public async Task<ApplicationServiceResult> DeleteRecipientAsync(int id, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ScadaDbContext>();
        var recipient = await dbContext.TelegramRecipients.FindAsync(new object[] { id }, cancellationToken);
        if (recipient == null) return ApplicationServiceResult.NotFound(new { message = "Destinatário não encontrado." });

        dbContext.TelegramRecipients.Remove(recipient);
        await dbContext.SaveChangesAsync(cancellationToken);
        return ApplicationServiceResult.Ok(new { message = "Destinatário excluído." });
    }

    public async Task<ApplicationServiceResult> SendTestAsync(TelegramTestRequest? request = null, CancellationToken cancellationToken = default)
    {
        var targets = await GetTargetsForTestAsync(request, cancellationToken);
        if (targets.Count == 0)
        {
            return ApplicationServiceResult.BadRequest(new { message = "Nenhum destino Telegram configurado." });
        }

        var text = string.IsNullOrWhiteSpace(request?.message)
            ? "Teste de alerta iioT AnalictY\n\nCanal Telegram configurado com sucesso."
            : request.message.Trim();
        var sent = 0;
        foreach (var target in targets)
        {
            if (await SendMessageAsync(target.BotToken, target.ChatId, text, cancellationToken)) sent++;
        }

        return sent > 0
            ? ApplicationServiceResult.Ok(new { message = $"Mensagem enviada para {sent} destino(s)." })
            : ApplicationServiceResult.BadRequest(new { message = "Falha ao enviar mensagem pelo Telegram. Verifique token, chat_id e acesso do bot." });
    }

    public async Task SendAlertAsync(Alert alert, int? connectionId = null, IReadOnlyCollection<int>? recipientIds = null, CancellationToken cancellationToken = default)
    {
        var targets = await GetActiveTargetsAsync(cancellationToken, connectionId, recipientIds);
        if (targets.Count == 0)
        {
            var token = _configuration["Telegram:BotToken"];
            var chatId = _configuration["Telegram:ChatId"];
            if (!connectionId.HasValue && (recipientIds == null || recipientIds.Count == 0) && IsConfigured(token, chatId))
            {
                targets.Add(new TelegramTarget(0, "Ambiente", token!, chatId!, (int)GetFallbackCooldown().TotalMinutes));
            }
        }

        if (targets.Count == 0) return;

        var text = $"""
        Alerta iioT AnalictY

        Severidade: {alert.Severity}
        Titulo: {alert.Title}
        Mensagem: {alert.Message}
        Maquina: {alert.MachineId ?? "Nao informada"}
        Horario: {alert.CreatedAt.ToLocalTime():dd/MM/yyyy HH:mm:ss}
        """;

        foreach (var target in targets)
        {
            var cooldownKey = $"{target.ConnectionId}:{target.ChatId}:{alert.AlertType}:{alert.Title}:{alert.MachineId ?? "global"}";
            await _telegramQueue.EnqueueAsync(
                new TelegramNotificationMessage(target.BotToken, target.ChatId, text, cooldownKey, target.CooldownMinutes),
                cancellationToken);
        }
    }

    private async Task<List<TelegramTarget>> GetActiveTargetsAsync(CancellationToken cancellationToken, int? connectionId = null, IReadOnlyCollection<int>? recipientIds = null)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ScadaDbContext>();
        var specificRecipientIds = recipientIds is { Count: > 0 }
            ? recipientIds.Where(id => id > 0).ToHashSet()
            : null;
        var rows = await (
            from connection in dbContext.TelegramConnections.AsNoTracking()
            join recipient in dbContext.TelegramRecipients.AsNoTracking()
                on connection.Id equals recipient.ConnectionId into recipients
            from recipient in recipients.DefaultIfEmpty()
            where connection.IsActive
                && connection.BotToken != ""
                && (!connectionId.HasValue || connection.Id == connectionId.Value)
                && (specificRecipientIds == null || (recipient != null && specificRecipientIds.Contains(recipient.Id)))
            select new { connection, recipient }
        ).ToListAsync(cancellationToken);

        return rows
            .Where(row => row.recipient == null
                ? !string.IsNullOrWhiteSpace(row.connection.DefaultChatId)
                : row.recipient.IsActive && !string.IsNullOrWhiteSpace(row.recipient.ChatId))
            .Select(row => new TelegramTarget(
                row.connection.Id,
                row.connection.Name,
                row.connection.BotToken,
                row.recipient?.ChatId ?? row.connection.DefaultChatId!,
                Math.Clamp(row.connection.CooldownMinutes, 1, 1440)))
            .ToList();
    }

    private async Task<List<TelegramTarget>> GetTargetsForTestAsync(TelegramTestRequest? request, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ScadaDbContext>();

        if (request?.recipient_id is int recipientId)
        {
            var row = await (
                from recipient in dbContext.TelegramRecipients.AsNoTracking()
                join connection in dbContext.TelegramConnections.AsNoTracking()
                    on recipient.ConnectionId equals connection.Id
                where recipient.Id == recipientId
                select new { connection, recipient }
            ).FirstOrDefaultAsync(cancellationToken);

            return row == null ? [] : [new TelegramTarget(row.connection.Id, row.connection.Name, row.connection.BotToken, row.recipient.ChatId, row.connection.CooldownMinutes)];
        }

        if (request?.connection_id is int connectionId)
        {
            var connection = await dbContext.TelegramConnections.AsNoTracking().FirstOrDefaultAsync(item => item.Id == connectionId, cancellationToken);
            if (connection == null || string.IsNullOrWhiteSpace(connection.DefaultChatId)) return [];
            return [new TelegramTarget(connection.Id, connection.Name, connection.BotToken, connection.DefaultChatId, connection.CooldownMinutes)];
        }

        return await GetActiveTargetsAsync(cancellationToken);
    }

    private async Task<bool> SendMessageAsync(string token, string chatId, string text, CancellationToken cancellationToken)
    {
        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(10);
            var response = await client.PostAsJsonAsync(
                $"https://api.telegram.org/bot{token}/sendMessage",
                new { chat_id = chatId, text, disable_web_page_preview = true },
                cancellationToken);

            if (response.IsSuccessStatusCode) return true;

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning("Telegram send failed: {StatusCode} {Body}", response.StatusCode, body);
            return false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Telegram send failed.");
            return false;
        }
    }

    private async Task<TelegramUpdatesResponse?> GetTelegramUpdatesAsync(string token, CancellationToken cancellationToken)
    {
        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(10);
            return await client.GetFromJsonAsync<TelegramUpdatesResponse>(
                $"https://api.telegram.org/bot{token}/getUpdates",
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Telegram getUpdates failed.");
            return null;
        }
    }

    private async Task<string?> GetBotLinkAsync(string token, CancellationToken cancellationToken)
    {
        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(10);
            var response = await client.GetFromJsonAsync<TelegramBotInfoResponse>(
                $"https://api.telegram.org/bot{token}/getMe",
                cancellationToken);
            return string.IsNullOrWhiteSpace(response?.Result?.Username)
                ? null
                : $"https://t.me/{response.Result.Username}";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Telegram getMe failed.");
            return null;
        }
    }

    private TimeSpan GetFallbackCooldown()
    {
        var minutes = _configuration.GetValue<int?>("Telegram:CooldownMinutes") ?? 15;
        return TimeSpan.FromMinutes(Math.Clamp(minutes, 1, 1440));
    }

    private static bool IsConfigured(string? token, string? chatId) =>
        !string.IsNullOrWhiteSpace(token) && !string.IsNullOrWhiteSpace(chatId);

    private static string? MaskChatId(string? chatId)
    {
        if (string.IsNullOrWhiteSpace(chatId)) return null;
        return chatId.Length <= 4 ? "****" : $"{new string('*', Math.Max(0, chatId.Length - 4))}{chatId[^4..]}";
    }

    private static string? MaskToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        return token.Length <= 8 ? "********" : $"{token[..4]}...{token[^4..]}";
    }

    private static string BuildChatName(TelegramChat chat)
    {
        var fullName = string.Join(" ", new[] { chat.FirstName, chat.LastName }.Where(item => !string.IsNullOrWhiteSpace(item)));
        if (!string.IsNullOrWhiteSpace(fullName)) return fullName;
        if (!string.IsNullOrWhiteSpace(chat.Title)) return chat.Title;
        if (!string.IsNullOrWhiteSpace(chat.Username)) return $"@{chat.Username}";
        return $"Telegram {chat.Id}";
    }

    private sealed record TelegramTarget(int ConnectionId, string ConnectionName, string BotToken, string ChatId, int CooldownMinutes);

    private sealed record TelegramUpdatesResponse(
        [property: JsonPropertyName("ok")] bool Ok,
        [property: JsonPropertyName("result")] List<TelegramUpdate> Result);

    private sealed record TelegramUpdate(
        [property: JsonPropertyName("message")] TelegramMessage? Message,
        [property: JsonPropertyName("edited_message")] TelegramMessage? EditedMessage,
        [property: JsonPropertyName("channel_post")] TelegramMessage? ChannelPost);

    private sealed record TelegramMessage(
        [property: JsonPropertyName("chat")] TelegramChat? Chat);

    private sealed record TelegramChat(
        [property: JsonPropertyName("id")] long? Id,
        [property: JsonPropertyName("type")] string? Type,
        [property: JsonPropertyName("title")] string? Title,
        [property: JsonPropertyName("username")] string? Username,
        [property: JsonPropertyName("first_name")] string? FirstName,
        [property: JsonPropertyName("last_name")] string? LastName);

    private sealed record TelegramBotInfoResponse(
        [property: JsonPropertyName("ok")] bool Ok,
        [property: JsonPropertyName("result")] TelegramBotInfo? Result);

    private sealed record TelegramBotInfo(
        [property: JsonPropertyName("username")] string? Username);
}
