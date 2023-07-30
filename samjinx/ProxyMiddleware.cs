using System.Text;
using Microsoft.AspNetCore.Http.Extensions;

namespace samjinx;

public class ProxyMiddleware
{
    private static readonly HttpClient _httpClient = new HttpClient();
    private static readonly Random Random = new Random();
    private readonly RequestDelegate _nextMiddleware;
    private readonly List<string> _servers;

    public ProxyMiddleware(RequestDelegate nextMiddleware, IConfiguration configuration)
    {
        _nextMiddleware = nextMiddleware;
        _servers = configuration["Servers"].Split(';', StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    public async Task Invoke(HttpContext context)
    {
        var targetUri = RewriteUri(context.Request);

        if (targetUri != null)
        {
            var targetRequestMessage = RewriteRequest(context, targetUri);

            using (var responseMessage = await _httpClient.SendAsync(targetRequestMessage, HttpCompletionOption.ResponseHeadersRead, context.RequestAborted))
            {
                context.Response.StatusCode = (int)responseMessage.StatusCode;

                RewriteResponseHeaders(context, responseMessage);

                await RewriteResponseContent(context, responseMessage);
            }

            return;
        }

        await _nextMiddleware(context);
    }

    private static async Task RewriteResponseContent(HttpContext context, HttpResponseMessage responseMessage)
    {
        var content = await responseMessage.Content.ReadAsByteArrayAsync();

        if (ContentType(responseMessage, "text/html") || ContentType(responseMessage, "text/javascript"))
        {
            var stringContent = Encoding.UTF8.GetString(content);

            // TODO: If needed modify content here.
            // eg.
            // stringContent = stringContent.Replace("Bob", "Dave");

            await context.Response.WriteAsync(stringContent, Encoding.UTF8);
        }
        else
        {
            await context.Response.Body.WriteAsync(content);
        }
    }

    private static bool ContentType(HttpResponseMessage responseMessage, string type)
    {
        var result = false;

        if (responseMessage.Content?.Headers?.ContentType != null)
        {
            result = responseMessage.Content.Headers.ContentType.MediaType == type;
        }

        return result;
    }

    private static HttpRequestMessage RewriteRequest(HttpContext context, Uri targetUri)
    {
        var requestMessage = new HttpRequestMessage();
        CloneOriginalRequest(context, requestMessage);

        // TODO: If needed manipulate request here.
        // eg. 
        // targetUri = new Uri(QueryHelpers.AddQueryString(targetUri.OriginalString, new Dictionary<string, string>() { { "name", "Bob Ross" } }));

        requestMessage.RequestUri = targetUri;
        requestMessage.Headers.Host = targetUri.Host;
        requestMessage.Method = GetMethod(context.Request.Method);

        return requestMessage;
    }

    private static void CloneOriginalRequest(HttpContext context, HttpRequestMessage requestMessage)
    {
        var requestMethod = context.Request.Method;

        if (!HttpMethods.IsGet(requestMethod) &&
            !HttpMethods.IsHead(requestMethod) &&
            !HttpMethods.IsDelete(requestMethod) &&
            !HttpMethods.IsTrace(requestMethod))
        {
            var streamContent = new StreamContent(context.Request.Body);
            requestMessage.Content = streamContent;
        }

        foreach (var header in context.Request.Headers)
        {
            requestMessage.Content?.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
        }
    }

    private static void RewriteResponseHeaders(HttpContext context, HttpResponseMessage responseMessage)
    {
        foreach (var header in responseMessage.Headers)
        {
            context.Response.Headers[header.Key] = header.Value.ToArray();
        }

        foreach (var header in responseMessage.Content.Headers)
        {
            context.Response.Headers[header.Key] = header.Value.ToArray();
        }
        context.Response.Headers.Remove("transfer-encoding");
    }

    private static HttpMethod GetMethod(string method)
    {
        if (HttpMethods.IsDelete(method)) return HttpMethod.Delete;
        if (HttpMethods.IsGet(method)) return HttpMethod.Get;
        if (HttpMethods.IsHead(method)) return HttpMethod.Head;
        if (HttpMethods.IsOptions(method)) return HttpMethod.Options;
        if (HttpMethods.IsPost(method)) return HttpMethod.Post;
        if (HttpMethods.IsPut(method)) return HttpMethod.Put;
        if (HttpMethods.IsTrace(method)) return HttpMethod.Trace;
        return new HttpMethod(method);
    }

    private Uri RewriteUri(HttpRequest request)
    {
        var nextHost = NextHost();
        Console.WriteLine($"Rerouting request {request.GetDisplayUrl()} to {nextHost}");
        request.Host = new HostString(nextHost);
        return new Uri(request.GetDisplayUrl());
    }

    private string NextHost()
    {
        lock (_servers)
        {
            // TODO: Change round robin strategy if required.
            return _servers[Random.Next(0, _servers.Count)];
        }
    }
}