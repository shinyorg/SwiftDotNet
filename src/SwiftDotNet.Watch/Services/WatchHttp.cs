using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;

namespace SwiftDotNet.Watch;

/// <summary>
/// <see cref="HttpMessageHandler"/> backed by <c>URLSession</c>. Apple restricts BSD-socket networking on
/// real watches for most apps, so the default <c>SocketsHttpHandler</c> only works in the simulator. Use
/// <see cref="WatchHttp.CreateClient"/>, or pass this handler to an <see cref="HttpClient"/>.
/// </summary>
public sealed class WatchHttpMessageHandler : HttpMessageHandler
{
    static readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> s_pending = new();
    static int s_subscribedGeneration = -1;

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(60);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        EnsureSubscribed();

        var id = Guid.NewGuid().ToString("N");
        var headers = new Dictionary<string, object?>();
        foreach (var h in request.Headers)
            headers[h.Key] = string.Join(", ", h.Value);

        string? body = null;
        if (request.Content is not null)
        {
            foreach (var h in request.Content.Headers)
                headers[h.Key] = string.Join(", ", h.Value);
            var bytes = await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            body = Convert.ToBase64String(bytes);
        }

        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        s_pending[id] = tcs;
        using var reg = cancellationToken.Register(() =>
        {
            if (s_pending.TryRemove(id, out var t))
            {
                WatchHost.CallNative("http.cancel", new Dictionary<string, object?> { ["id"] = id });
                t.TrySetCanceled(cancellationToken);
            }
        });

        WatchHost.CallNative("http.send", new Dictionary<string, object?>
        {
            ["id"] = id,
            ["method"] = request.Method.Method,
            ["url"] = request.RequestUri!.AbsoluteUri,
            ["headers"] = headers,
            ["body"] = body,
            ["timeout"] = Timeout.TotalSeconds,
        });

        var result = await tcs.Task.ConfigureAwait(false);

        if (result.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String)
            throw new HttpRequestException(error.GetString());

        var response = new HttpResponseMessage((HttpStatusCode)result.GetProperty("status").GetInt32())
        {
            RequestMessage = request,
            Content = new ByteArrayContent(
                result.TryGetProperty("body", out var b) && b.ValueKind == JsonValueKind.String
                    ? Convert.FromBase64String(b.GetString()!)
                    : []),
        };
        if (result.TryGetProperty("headers", out var rh) && rh.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in rh.EnumerateObject())
            {
                var value = p.Value.GetString() ?? "";
                if (!response.Headers.TryAddWithoutValidation(p.Name, value))
                    response.Content.Headers.TryAddWithoutValidation(p.Name, value);
            }
        }
        return response;
    }

    static void EnsureSubscribed()
    {
        var current = WatchHost.Generation;
        if (Interlocked.Exchange(ref s_subscribedGeneration, current) == current)
            return;
        WatchHost.OnNativeEvent("http.response", e =>
        {
            var id = e.GetProperty("id").GetString()!;
            if (s_pending.TryRemove(id, out var tcs))
                tcs.TrySetResult(e);
        });
    }
}

public static class WatchHttp
{
    /// <summary>An <see cref="HttpClient"/> that works on real watch hardware (<c>URLSession</c> underneath).</summary>
    public static HttpClient CreateClient() => new(new WatchHttpMessageHandler());
}
