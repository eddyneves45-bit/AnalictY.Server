using System.Collections.Concurrent;
using System.Net.Http.Json;

namespace Scada.Api.Services;

internal sealed class TelegramNotificationWorker : BackgroundService
{
    private readonly ITelegramNotificationQueue _queue;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<TelegramNotificationWorker> _logger;
    private readonly ConcurrentDictionary<string, DateTime> _lastSentAt = new();

    public TelegramNotificationWorker(
        ITelegramNotificationQueue queue,
        IHttpClientFactory httpClientFactory,
        ILogger<TelegramNotificationWorker> logger)
    {
        _queue = queue;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var message in _queue.ReadAllAsync(stoppingToken))
        {
            if (IsCoolingDown(message))
            {
                continue;
            }

            if (await SendMessageAsync(message, stoppingToken))
            {
                _lastSentAt[message.CooldownKey] = DateTime.UtcNow;
            }
        }
    }

    private bool IsCoolingDown(TelegramNotificationMessage message)
    {
        var cooldown = TimeSpan.FromMinutes(Math.Clamp(message.CooldownMinutes, 1, 1440));
        return _lastSentAt.TryGetValue(message.CooldownKey, out var lastSentAt)
            && DateTime.UtcNow - lastSentAt < cooldown;
    }

    private async Task<bool> SendMessageAsync(TelegramNotificationMessage message, CancellationToken cancellationToken)
    {
        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(10);
            var response = await client.PostAsJsonAsync(
                $"https://api.telegram.org/bot{message.BotToken}/sendMessage",
                new { chat_id = message.ChatId, text = message.Text, disable_web_page_preview = true },
                cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return true;
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning("Telegram worker send failed: {StatusCode} {Body}", response.StatusCode, body);
            return false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Telegram worker send failed.");
            return false;
        }
    }
}
