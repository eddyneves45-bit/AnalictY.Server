using Scada.Api.Services;

public static class DriverEndpoints
{
    public static WebApplication MapDriverEndpoints(this WebApplication app)
    {
        app.MapGet("/api/drivers/opcua/status", async (string endpointUrl, IDriverStatusService driverStatusService) =>
        {
            return Results.Ok(await driverStatusService.CheckOpcuaStatusAsync(endpointUrl));
        })
        .WithName("CheckOpcuaDriverStatus");

        app.MapPost("/api/drivers/opcua/connect", async (IDriverStatusService driverStatusService, CancellationToken cancellationToken) =>
        {
            return (await driverStatusService.ConnectActiveOpcuaAsync(cancellationToken)).ToHttpResult();
        })
        .WithName("ConnectOpcuaDriverWithConfig");

        app.MapGet("/api/drivers/mqtt/status", async (string brokerUrl, string clientId, IDriverStatusService driverStatusService) =>
        {
            return Results.Ok(await driverStatusService.CheckMqttStatusAsync(brokerUrl, clientId));
        })
        .WithName("CheckMqttDriverStatus");

        app.MapGet("/api/drivers/modbus/status", async (string host, int port, IDriverStatusService driverStatusService) =>
        {
            return Results.Ok(await driverStatusService.CheckModbusStatusAsync(host, port));
        })
        .WithName("CheckModbusDriverStatus");

        return app;
    }
}
