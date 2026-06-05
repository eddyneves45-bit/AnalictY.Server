namespace Scada.Api.Services;

internal sealed record ApplicationServiceResult(int StatusCode, object? Value = null)
{
    public static ApplicationServiceResult Ok(object? value) => new(StatusCodes.Status200OK, value);
    public static ApplicationServiceResult BadRequest(object? value) => new(StatusCodes.Status400BadRequest, value);
    public static ApplicationServiceResult NotFound(object? value = null) => new(StatusCodes.Status404NotFound, value);

    public IResult ToHttpResult()
    {
        return Value == null
            ? Results.StatusCode(StatusCode)
            : Results.Json(Value, statusCode: StatusCode);
    }
}
