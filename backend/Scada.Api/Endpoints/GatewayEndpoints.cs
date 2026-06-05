using Scada.Api.Services;
using Scada.Gateway.Interfaces;

public static class GatewayEndpoints
{
    public static WebApplication MapGatewayEndpoints(this WebApplication app)
    {
        app.MapGet("/api/gateway/health", (IGatewayAppService gatewayService) =>
        {
            return Results.Ok(gatewayService.GetGatewayHealth());
        })
        .WithName("GetGatewayHealth");

        app.MapGet("/api/gateway/health/{moduleName}", async (string moduleName, IGatewayAppService gatewayService) =>
        {
            return Results.Ok(await gatewayService.GetModuleHealthAsync(moduleName));
        })
        .WithName("GetModuleHealth");

        app.MapPost("/api/gateway/route", async (GatewayRequest request, IGatewayAppService gatewayService) =>
        {
            return Results.Ok(await gatewayService.RouteRequestAsync(request));
        })
        .WithName("RouteRequest");

        return app;
    }
}
