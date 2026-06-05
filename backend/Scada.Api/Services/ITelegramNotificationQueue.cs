namespace Scada.Api.Services;

internal interface ITelegramNotificationQueue
{
    ValueTask<bool> EnqueueAsync(TelegramNotificationMessage message, CancellationToken cancellationToken = default);
    IAsyncEnumerable<TelegramNotificationMessage> ReadAllAsync(CancellationToken cancellationToken = default);
}

internal sealed record TelegramNotificationMessage(
    string BotToken,
    string ChatId,
    string Text,
    string CooldownKey,
    int CooldownMinutes);
