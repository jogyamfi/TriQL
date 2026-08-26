using System.Net;
using TriQL.Client.Internal;

namespace TriQL.Client.Tests;

// Response/handler test doubles are handed off across queues and delegate closures whose eventual
// disposal (by RedirectHandler internals or the test itself) the analyzer cannot trace.
#pragma warning disable CA2000
public sealed class RedirectHandlerTests
{
    [Fact]
    public async Task SendAsync_FollowsRedirect_ForGetRequests()
    {
        var responses = new Queue<HttpResponseMessage>();
        responses.Enqueue(Redirect("https://trino.example.com/v1/info2"));
        responses.Enqueue(new HttpResponseMessage(HttpStatusCode.OK));

        using var handler = new RedirectHandler(new QueueHandler(responses));
        using var invoker = new HttpMessageInvoker(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://trino.example.com/v1/info");

        using var response = await invoker.SendAsync(request, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SendAsync_DoesNotFollowRedirect_ForPostRequests()
    {
        var responses = new Queue<HttpResponseMessage>();
        responses.Enqueue(Redirect("https://trino.example.com/v1/statement2"));

        using var handler = new RedirectHandler(new QueueHandler(responses));
        using var invoker = new HttpMessageInvoker(handler);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://trino.example.com/v1/statement");

        using var response = await invoker.SendAsync(request, CancellationToken.None);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
    }

    [Fact]
    public async Task SendAsync_DoesNotFollowRedirect_AcrossHttpsToHttpDowngrade()
    {
        var responses = new Queue<HttpResponseMessage>();
        responses.Enqueue(Redirect("http://trino.example.com/v1/info"));

        using var handler = new RedirectHandler(new QueueHandler(responses));
        using var invoker = new HttpMessageInvoker(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://trino.example.com/v1/info");

        using var response = await invoker.SendAsync(request, CancellationToken.None);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
    }

    [Fact]
    public async Task SendAsync_StopsAfterMaxRedirects()
    {
        var callCount = 0;
        using var handler = new RedirectHandler(new DelegateHandler(_ =>
        {
            callCount++;
            return Redirect($"https://trino.example.com/v1/info/{callCount}");
        }));
        using var invoker = new HttpMessageInvoker(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://trino.example.com/v1/info");

        using var response = await invoker.SendAsync(request, CancellationToken.None);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal(RedirectHandler.MaxRedirects + 1, callCount);
    }

    private static HttpResponseMessage Redirect(string location)
    {
        var response = new HttpResponseMessage(HttpStatusCode.Found);
        response.Headers.Location = new Uri(location);
        return response;
    }

    private sealed class QueueHandler(Queue<HttpResponseMessage> responses) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responses.Count > 0 ? responses.Dequeue() : new HttpResponseMessage(HttpStatusCode.OK));
    }

    private sealed class DelegateHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }
}
#pragma warning restore CA2000
