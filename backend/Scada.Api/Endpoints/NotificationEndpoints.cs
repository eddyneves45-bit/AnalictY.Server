using Scada.Api.Services;

public static class NotificationEndpoints
{
    public static WebApplication MapNotificationEndpoints(this WebApplication app)
    {
        app.MapGet("/api/notifications/telegram/status", async (
            ITelegramNotificationService telegramService,
            CancellationToken cancellationToken) =>
        {
            return Results.Ok(await telegramService.GetStatusAsync(cancellationToken));
        })
        .WithName("GetTelegramNotificationStatus");

        app.MapPost("/api/notifications/telegram/test", async (
            TelegramTestRequest? request,
            ITelegramNotificationService telegramService,
            CancellationToken cancellationToken) =>
        {
            return (await telegramService.SendTestAsync(request, cancellationToken)).ToHttpResult();
        })
        .WithName("TestTelegramNotification");

        app.MapGet("/api/notifications/telegram/connections", async (
            ITelegramNotificationService telegramService,
            CancellationToken cancellationToken) =>
        {
            return Results.Ok(await telegramService.ListConnectionsAsync(cancellationToken));
        })
        .WithName("ListTelegramConnections");

        app.MapPost("/api/notifications/telegram/connections", async (
            TelegramConnectionRequest request,
            ITelegramNotificationService telegramService,
            CancellationToken cancellationToken) =>
        {
            return (await telegramService.UpsertConnectionAsync(null, request, cancellationToken)).ToHttpResult();
        })
        .WithName("CreateTelegramConnection");

        app.MapPut("/api/notifications/telegram/connections/{id:int}", async (
            int id,
            TelegramConnectionRequest request,
            ITelegramNotificationService telegramService,
            CancellationToken cancellationToken) =>
        {
            return (await telegramService.UpsertConnectionAsync(id, request, cancellationToken)).ToHttpResult();
        })
        .WithName("UpdateTelegramConnection");

        app.MapDelete("/api/notifications/telegram/connections/{id:int}", async (
            int id,
            ITelegramNotificationService telegramService,
            CancellationToken cancellationToken) =>
        {
            return (await telegramService.DeleteConnectionAsync(id, cancellationToken)).ToHttpResult();
        })
        .WithName("DeleteTelegramConnection");

        app.MapGet("/api/notifications/telegram/recipients", async (
            int? connection_id,
            ITelegramNotificationService telegramService,
            CancellationToken cancellationToken) =>
        {
            return Results.Ok(await telegramService.ListRecipientsAsync(connection_id, cancellationToken));
        })
        .WithName("ListTelegramRecipients");

        app.MapGet("/api/notifications/telegram/candidates", async (
            int? connection_id,
            ITelegramNotificationService telegramService,
            CancellationToken cancellationToken) =>
        {
            return (await telegramService.CaptureRecipientsAsync(connection_id, cancellationToken)).ToHttpResult();
        })
        .WithName("CaptureTelegramRecipients");

        app.MapPost("/api/notifications/telegram/recipients", async (
            TelegramRecipientRequest request,
            ITelegramNotificationService telegramService,
            CancellationToken cancellationToken) =>
        {
            return (await telegramService.UpsertRecipientAsync(null, request, cancellationToken)).ToHttpResult();
        })
        .WithName("CreateTelegramRecipient");

        app.MapPut("/api/notifications/telegram/recipients/{id:int}", async (
            int id,
            TelegramRecipientRequest request,
            ITelegramNotificationService telegramService,
            CancellationToken cancellationToken) =>
        {
            return (await telegramService.UpsertRecipientAsync(id, request, cancellationToken)).ToHttpResult();
        })
        .WithName("UpdateTelegramRecipient");

        app.MapDelete("/api/notifications/telegram/recipients/{id:int}", async (
            int id,
            ITelegramNotificationService telegramService,
            CancellationToken cancellationToken) =>
        {
            return (await telegramService.DeleteRecipientAsync(id, cancellationToken)).ToHttpResult();
        })
        .WithName("DeleteTelegramRecipient");

        return app;
    }
}
