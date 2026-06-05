namespace Scada.Api.Services;

internal sealed record TelegramConnectionRequest(
    string name,
    string? bot_token,
    string? default_chat_id,
    bool is_active,
    int cooldown_minutes);

internal sealed record TelegramRecipientRequest(
    int connection_id,
    string name,
    string chat_id,
    string destination_type,
    bool is_active);

internal sealed record TelegramTestRequest(
    int? connection_id,
    int? recipient_id,
    string? message);
