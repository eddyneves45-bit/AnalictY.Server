using System.Net.Http.Headers;

namespace Scada.Api.Services;

public sealed class FrontendProxyMiddleware
{
    private static readonly HashSet<string> SkippedRequestHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Host",
        "Connection",
        "Content-Length",
        "Expect",
        "Transfer-Encoding",
        "Upgrade"
    };

    private static readonly HashSet<string> SkippedResponseHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Connection",
        "Keep-Alive",
        "Proxy-Authenticate",
        "Proxy-Authorization",
        "TE",
        "Trailer",
        "Transfer-Encoding",
        "Upgrade"
    };

    private readonly RequestDelegate _next;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<FrontendProxyMiddleware> _logger;
    private readonly Uri _frontendBaseUri;

    public FrontendProxyMiddleware(
        RequestDelegate next,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<FrontendProxyMiddleware> logger)
    {
        _next = next;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _frontendBaseUri = new Uri(configuration["AnalictY:FrontendProxyUrl"] ?? "http://127.0.0.1:3000");
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (ShouldSkipProxy(context.Request.Path))
        {
            await _next(context);
            return;
        }

        var client = _httpClientFactory.CreateClient(nameof(FrontendProxyMiddleware));
        using var proxyRequest = CreateProxyRequest(context);

        try
        {
            using var response = await client.SendAsync(
                proxyRequest,
                HttpCompletionOption.ResponseHeadersRead,
                context.RequestAborted);

            context.Response.StatusCode = (int)response.StatusCode;
            CopyResponseHeaders(context.Response, response);
            await response.Content.CopyToAsync(context.Response.Body, context.RequestAborted);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha ao encaminhar requisicao para o frontend local.");
            context.Response.StatusCode = StatusCodes.Status502BadGateway;
            await context.Response.WriteAsync("Frontend local indisponivel.", context.RequestAborted);
        }
    }

    private static bool ShouldSkipProxy(PathString path)
    {
        return path.StartsWithSegments("/api") ||
            path.StartsWithSegments("/hubs") ||
            path.StartsWithSegments("/ws") ||
            path.StartsWithSegments("/swagger");
    }

    private HttpRequestMessage CreateProxyRequest(HttpContext context)
    {
        var targetUri = new UriBuilder(_frontendBaseUri)
        {
            Path = context.Request.Path,
            Query = context.Request.QueryString.HasValue
                ? context.Request.QueryString.Value![1..]
                : string.Empty
        }.Uri;

        var request = new HttpRequestMessage(new HttpMethod(context.Request.Method), targetUri);

        if (!HttpMethods.IsGet(context.Request.Method) &&
            !HttpMethods.IsHead(context.Request.Method) &&
            !HttpMethods.IsDelete(context.Request.Method) &&
            !HttpMethods.IsTrace(context.Request.Method))
        {
            request.Content = new StreamContent(context.Request.Body);
            if (!string.IsNullOrWhiteSpace(context.Request.ContentType))
            {
                request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(context.Request.ContentType);
            }
        }

        foreach (var header in context.Request.Headers)
        {
            if (SkippedRequestHeaders.Contains(header.Key))
            {
                continue;
            }

            if (!request.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray()) && request.Content is not null)
            {
                request.Content.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
            }
        }

        request.Headers.TryAddWithoutValidation("X-Forwarded-Host", context.Request.Host.Value);
        request.Headers.TryAddWithoutValidation("X-Forwarded-Proto", context.Request.Scheme);
        return request;
    }

    private static void CopyResponseHeaders(HttpResponse destination, HttpResponseMessage source)
    {
        foreach (var header in source.Headers)
        {
            if (!SkippedResponseHeaders.Contains(header.Key))
            {
                destination.Headers[header.Key] = header.Value.ToArray();
            }
        }

        foreach (var header in source.Content.Headers)
        {
            if (!SkippedResponseHeaders.Contains(header.Key))
            {
                destination.Headers[header.Key] = header.Value.ToArray();
            }
        }

        destination.Headers.Remove("transfer-encoding");
    }
}
